﻿using MakeItMeta.Tools.Results;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace MakeItMeta.Tools;

public class MetaMaker
{
    public Result<IReadOnlyList<MemoryStream>> MakeItMeta(Stream[] targetAssemblies, InjectionConfig? injectionConfig = null)
    {
        var targetModules = targetAssemblies
            .Select(ModuleDefinition.ReadModule)
            .ToArray();

        var injectableModules = injectionConfig?
            .AdditionalAssemblies?
            .Select(ModuleDefinition.ReadModule)
            .ToArray() ?? Array.Empty<ModuleDefinition>();

        var resultAssemblies = new List<MemoryStream>(targetAssemblies.Length);
        foreach (var targetModule in targetModules)
        {
            var allModules = new List<ModuleDefinition>();
            allModules.Add(targetModule);
            allModules.AddRange(injectableModules);

            var types = allModules
                .SelectMany(o => o.Types)
                .ToArray();

            if (injectionConfig is not null)
            {
                var injectAttributeError = InjectAttributes(types, injectionConfig).Unwrap();
                if (injectAttributeError)
                {
                    return injectAttributeError;
                }
            }

            var injectionError = InjectInterceptorBaseOnAttributes(types, targetModule).Unwrap();
            if (injectionError)
            {
                return injectionError;
            }

            var newAssembly = new MemoryStream();
            targetModule.Write(newAssembly);
            newAssembly.Seek(0, SeekOrigin.Begin);
            resultAssemblies.Add(newAssembly);
        }

        return resultAssemblies;
    }

    private Result InjectInterceptorBaseOnAttributes(TypeDefinition[] types, ModuleDefinition targetModule)
    {
        var methodsWithMetaAttributes = types
            .SelectMany(o => o.Methods)
            .Where(o => o.HasBody && !o.IsConstructor)
            .Where(MethodThatHasMetaAttributeOrContainingTypeHasMetaAttribute)
            .ToArray();

        foreach (var method in methodsWithMetaAttributes)
        {
            var targetModuleName = targetModule.Assembly.FullName;
            var fullMethodName = $"{method.DeclaringType.FullName}.{method.Name}";

            if (method.Body is null)
            {
                return new Error("METHOD_MISSING_BODY", $"The method '{fullMethodName}' missing body");
            }

            if (method.Body.Instructions.Count == 0)
            {
                return new Error("METHOD_MISSING_INSTRUCTIONS", $"The method '{fullMethodName}' missing instructions");
            }

            if (method.Parameters.Any(
                    o => o.IsOut || o.ParameterType.IsByReference))
            {
                var message = $"At the moment method '{fullMethodName}' is not supported. It has generics or by references parameters";
                return new Error("METHOD_IS_NOT_SUPPORTED", message);
            }

            var typeMetaAttributes = method.DeclaringType.CustomAttributes
                .Where(a => a.AttributeType.Resolve().BaseType.Name == "MetaAttribute")
                .ToArray();

            var methodMetaAttributes = method.CustomAttributes
                .Where(a => a.AttributeType.Resolve().BaseType.Name == "MetaAttribute")
                .ToArray();

            var metaAttributes = typeMetaAttributes
                .Concat(methodMetaAttributes)
                .DistinctBy(o => o.AttributeType.Name)
                .ToArray();

            var attributeValidationError = ValidateAttributes(metaAttributes).Unwrap();
            if (attributeValidationError)
            {
                return attributeValidationError;
            }

            foreach (var metaAttribute in metaAttributes)
            {
                var attributeType = metaAttribute.AttributeType.Resolve();
                var onEntryMethod = targetModule.ImportReference(attributeType.Methods.Single(o => o.Name == "OnEntry"));
                var onExitMethod = targetModule.ImportReference(attributeType.Methods.Single(o => o.Name == "OnExit"));
                var onEntryReturnType = method.Module.ImportReference(onEntryMethod.ReturnType.Resolve());
                var onEnterVoidReturn = onEntryReturnType.FullName == "System.Void";
                if (!onEnterVoidReturn)
                {
                    var onExitAcceptType = method.Module.ImportReference(onExitMethod.Parameters.Last().ParameterType.Resolve());
                    if (onEntryReturnType.FullName != onExitAcceptType.FullName)
                    {
                        return new Error(
                            "ATTRIBUTE_NOT_FOLLOW_CONVENTION",
                            "The OnExit method has to accept the return value from OnEnter as last parameter");
                    }
                }

                var parameters = method.Parameters
                    .Where(o => !o.IsOut || !o.ParameterType.IsByReference)
                    .ToArray();

                var il = method.Body.GetILProcessor();

                var firstInstruction = method.Body.Instructions.First();
                var lastInstruction = method.Body.Instructions.Last();

                var onEnterReturnVariable = onEnterVoidReturn
                    ? null
                    : new VariableDefinition(onEntryReturnType);

                if (onEnterReturnVariable is not null)
                {
                    method.Body.Variables.Add(onEnterReturnVariable);
                }

                il.InsertBefore(firstInstruction, method.HasThis ? il.Create(OpCodes.Ldarg_0) : il.Create(OpCodes.Ldnull));
                il.InsertBefore(firstInstruction, il.Create(OpCodes.Ldstr, targetModuleName));
                il.InsertBefore(firstInstruction, il.Create(OpCodes.Ldstr, fullMethodName));

                il.InsertBefore(firstInstruction, il.Create(OpCodes.Ldc_I4, parameters.Length));
                il.InsertBefore(firstInstruction, il.Create(OpCodes.Newarr, targetModule.ImportReference(typeof(object))));
                for (var i = 0; i < parameters.Length; i++)
                {
                    il.InsertBefore(firstInstruction, il.Create(OpCodes.Dup));
                    il.InsertBefore(firstInstruction, il.Create(OpCodes.Ldc_I4, i));
                    il.InsertBefore(firstInstruction, il.Create(OpCodes.Ldarg, parameters[i]));
                    il.InsertBefore(firstInstruction, il.Create(OpCodes.Box, parameters[i].ParameterType));
                    il.InsertBefore(firstInstruction, il.Create(OpCodes.Stelem_Ref));
                }

                il.InsertBefore(firstInstruction, il.Create(OpCodes.Call, onEntryMethod));

                if (onEnterReturnVariable is not null)
                {
                    il.InsertBefore(firstInstruction, il.Create(OpCodes.Stloc, onEnterReturnVariable));
                }

                var onExitInstruction = method.HasThis ? il.Create(OpCodes.Ldarg_0) : il.Create(OpCodes.Ldnull);
                il.InsertBefore(lastInstruction, onExitInstruction);
                il.InsertBefore(lastInstruction, il.Create(OpCodes.Ldstr, targetModuleName));
                il.InsertBefore(lastInstruction, il.Create(OpCodes.Ldstr, fullMethodName));

                if (onEnterReturnVariable is not null)
                {
                    il.InsertBefore(lastInstruction, il.Create(OpCodes.Ldloc, onEnterReturnVariable));
                }

                il.InsertBefore(lastInstruction, il.Create(OpCodes.Call, onExitMethod));

                ReplaceJumps(method.Body);
            }
        }

        return Result.Success();
    }

    private Result ValidateAttributes(CustomAttribute[] metaAttributes)
    {
        foreach (var metaAttribute in metaAttributes)
        {
            var onEntry = metaAttribute.AttributeType.Resolve()
                .GetMethods()
                .First(o => o.Name == "OnEntry");

            var onExit = metaAttribute.AttributeType.Resolve()
                .GetMethods()
                .First(o => o.Name == "OnExit");

            var parameters = new[]
            {
                (Name: "this", Type: "System.Object"),
                (Name: "assemblyFullName", Type: "System.String"),
                (Name: "methodName", Type: "System.String"),
            };

            for (int i = 0; i < parameters.Length; i++)
            {
                var onEntryArgument = parameters[i];
                if (onEntry.Parameters[i].Name != onEntryArgument.Name ||
                    onEntry.Parameters[i].ParameterType.FullName != onEntryArgument.Type)
                {
                    return new Error(
                        "INVALID_META_ATTRIBUTE",
                        $"[{metaAttribute.AttributeType.FullName}] The OnEntry '{i}' parameter has to be '{onEntryArgument.Type} {onEntryArgument.Name}'");
                }
            }
            
            for (int i = 0; i < parameters.Length; i++)
            {
                var onExitParameter = parameters[i];
                if (onExit.Parameters[i].Name != onExitParameter.Name ||
                    onExit.Parameters[i].ParameterType.FullName != onExitParameter.Type)
                {
                    return new Error(
                        "INVALID_META_ATTRIBUTE",
                        $"[{metaAttribute.AttributeType.FullName}] The OnExit '{i}' parameter has to be '{onExitParameter.Type} {onExitParameter.Name}'");
                }
            }

            if (onEntry.ReturnType.FullName != "System.Void")
            {
                if (onExit.Parameters.Count != 4)
                {
                    return new Error(
                        "INVALID_META_ATTRIBUTE",
                        $"[{metaAttribute.AttributeType.FullName}] The OnEnter returns '{onEntry.ReturnType.FullName}'. The OnExit has to accept is as last parameter.");

                }

                var last = onExit.Parameters.Last();
                if (last.ParameterType.FullName != onEntry.ReturnType.FullName)
                {
                    return new Error(
                        "INVALID_META_ATTRIBUTE",
                        $"[{metaAttribute.AttributeType.FullName}] The OnEnter returns '{onEntry.ReturnType.FullName}'. The OnExit has to accept is as last parameter.");

                }
            }
        }
        
        return Result.Success();
    }

    private static bool MethodThatHasMetaAttributeOrContainingTypeHasMetaAttribute(MethodDefinition method)
        => method.CustomAttributes.Any(a => a.AttributeType.Resolve().BaseType.Name == "MetaAttribute") ||
           method.DeclaringType.CustomAttributes.Any(
               a => a.AttributeType.Resolve().BaseType.Name == "MetaAttribute");

    private Result InjectAttributes(TypeDefinition[] types, InjectionConfig injectionConfig)
    {
        if (injectionConfig.Entries is null)
        {
            return Result.Success();
        }

        var attributesSet = injectionConfig.Entries
            .Select(o => o.Attribute)
            .ToHashSet();

        var metaAttributes = types
            .Where(o => attributesSet.Contains(o.FullName))
            .ToDictionary(o => o.FullName);

        foreach (var entry in injectionConfig.Entries)
        {
            var entriesByType = entry.Types?
                .ToDictionary(o => o.Name);

            var typesToProcess = types
                .Where(o => entriesByType?.ContainsKey(o.FullName) ?? true)
                .Where(o => !o.FullName.Contains('<'))
                .Where(o => !o.FullName.StartsWith("System."))
                .Where(o => !o.FullName.StartsWith("Microsoft."))
                .ToDictionary(o => o.FullName);

            var missingTypes = entriesByType?
                                   .Where(o => !typesToProcess.ContainsKey(o.Key))
                                   .Select(o => o.Key)
                                   .ToArray()
                               ?? Array.Empty<string>();

            if (missingTypes.Any())
            {
                return missingTypes
                    .Select(o => new Error("MISSING_TYPE", $"The type '{o}' is missing"))
                    .ToArray();
            }

            var entryTypes = entry.Types ?? typesToProcess
                .Select(o => new InjectionTypeEntry(o.Key, null))
                .ToArray();

            foreach (var typeEntry in entryTypes)
            {
                if (metaAttributes.TryGetValue(typeEntry.Name, out var _))
                {
                    continue;
                }
                
                if (!metaAttributes.TryGetValue(entry.Attribute, out var injectableAttribute))
                {
                    return Result.Error(new Error("FAILED_TO_FIND_ATTRIBUTE", $"Failed to find meta attribute '{entry.Attribute}'"));
                }

                var typeDefinition = typesToProcess[typeEntry.Name];
                var methodDefinition = injectableAttribute.GetConstructors().First();
                var attributeConstructor = typeDefinition.Module.ImportReference(methodDefinition);
                if (typeEntry.Methods is null)
                {
                    typeDefinition.CustomAttributes.Add(new CustomAttribute(attributeConstructor));
                    continue;
                }

                var injectableMethods = typeDefinition.Methods
                    .IntersectBy(typeEntry.Methods, o => o.Name)
                    .ToDictionary(o => o.Name);

                var missingMethods = typeEntry.Methods
                    .Where(o => !injectableMethods.ContainsKey(o))
                    .ToArray();

                if (missingMethods.Any())
                {
                    return missingMethods
                        .Select(o => new Error("MISSING_METHOD", $"The method '{o}' is missing in type '{typeEntry.Name}'"))
                        .ToArray();
                }

                foreach (var injectableMethod in injectableMethods.Values)
                {
                    injectableMethod.CustomAttributes.Add(new CustomAttribute(attributeConstructor));
                }
            }
        }

        return Result.Success();
    }

    void ReplaceJumps(MethodBody methodBody)
    {
        foreach (var instruction in methodBody.Instructions)
        {
            if (instruction.OpCode == OpCodes.Br_S)
            {
                instruction.OpCode = OpCodes.Br;
                continue;
            }

            if (instruction.OpCode == OpCodes.Leave_S)
            {
                instruction.OpCode = OpCodes.Leave;
                continue;
            }

            if (instruction.OpCode == OpCodes.Beq_S)
            {
                instruction.OpCode = OpCodes.Beq;
                continue;
            }

            if (instruction.OpCode == OpCodes.Bne_Un_S)
            {
                instruction.OpCode = OpCodes.Bne_Un;
                continue;
            }

            if (instruction.OpCode == OpCodes.Bge_S)
            {
                instruction.OpCode = OpCodes.Bge;
                continue;
            }

            if (instruction.OpCode == OpCodes.Bge_Un_S)
            {
                instruction.OpCode = OpCodes.Bge_Un;
                continue;
            }

            if (instruction.OpCode == OpCodes.Bgt_S)
            {
                instruction.OpCode = OpCodes.Bgt;
                continue;
            }

            if (instruction.OpCode == OpCodes.Bgt_Un_S)
            {
                instruction.OpCode = OpCodes.Bgt_Un;
                continue;
            }

            if (instruction.OpCode == OpCodes.Ble_S)
            {
                instruction.OpCode = OpCodes.Ble;
                continue;
            }

            if (instruction.OpCode == OpCodes.Ble_Un_S)
            {
                instruction.OpCode = OpCodes.Ble_Un;
                continue;
            }

            if (instruction.OpCode == OpCodes.Blt_S)
            {
                instruction.OpCode = OpCodes.Blt;
                continue;
            }

            if (instruction.OpCode == OpCodes.Blt_Un_S)
            {
                instruction.OpCode = OpCodes.Blt_Un;
                continue;
            }

            if (instruction.OpCode == OpCodes.Bne_Un_S)
            {
                instruction.OpCode = OpCodes.Bne_Un;
                continue;
            }

            if (instruction.OpCode == OpCodes.Brfalse_S)
            {
                instruction.OpCode = OpCodes.Brfalse;
                continue;
            }

            if (instruction.OpCode == OpCodes.Brtrue_S)
            {
                instruction.OpCode = OpCodes.Brtrue;
                continue;
            }

            if (instruction.OpCode == OpCodes.Beq_S)
            {
                instruction.OpCode = OpCodes.Beq;
            }
        }
    }
}