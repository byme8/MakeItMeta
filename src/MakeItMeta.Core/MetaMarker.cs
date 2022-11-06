using MakeItMeta.Core.Results;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace MakeItMeta.Core;

public class MetaMaker
{
    public Result<MemoryStream> MakeItMeta(Stream assembly, InjectionConfig? injectionConfig = null)
    {
        var targetModule = ModuleDefinition.ReadModule(assembly);
        var injectableModules = injectionConfig?
            .AdditionalAssemblies?
            .Select(ModuleDefinition.ReadModule) ?? Array.Empty<ModuleDefinition>();

        var allModules = new List<ModuleDefinition>();
        allModules.Add(targetModule);
        allModules.AddRange(injectableModules);

        var types = allModules
            .SelectMany(o => o.Types)
            .ToArray();

        if (injectionConfig is not null)
        {
            var errors = InjectAttributes(types, injectionConfig).Unwrap();
            if (errors)
            {
                return errors;
            }
        }

        InjectInterceptorBaseOnAttributes(types, targetModule);

        var newAssembly = new MemoryStream();
        targetModule.Write(newAssembly);

        return newAssembly;
    }

    private void InjectInterceptorBaseOnAttributes(TypeDefinition[] types, ModuleDefinition targetModule)
    {
        var methodsWithMetaAttributes = types
            .SelectMany(o => o.Methods)
            .Where(o => o.HasBody)
            .Where(MethodThatHasMetaAttributeOrContainingTypeHasMetaAttribute)
            .ToArray();

        foreach (var method in methodsWithMetaAttributes)
        {
            if (method.Body is null)
            {
                continue;
            }

            if (method.Body.Instructions.Count == 0)
            {
                continue;
            }

            if (method.Parameters.Any(
                    o => o.IsOut || o.ParameterType.IsByReference || o.ParameterType.IsGenericInstance))
            {
                continue;
            }

            var fullMethodName = $"{method.DeclaringType.FullName}.{method.Name}";

            var typeMetaAttributes = method.DeclaringType.CustomAttributes
                .Where(a => a.AttributeType.Resolve().BaseType.Name == "MetaAttribute")
                .Select(o => o.AttributeType)
                .ToArray();

            var methodMetaAttributes = method.CustomAttributes
                .Where(a => a.AttributeType.Resolve().BaseType.Name == "MetaAttribute")
                .Select(o => o.AttributeType)
                .ToArray();

            foreach (var metaAttribute in typeMetaAttributes.Concat(methodMetaAttributes).DistinctBy(o => o.Name))
            {
                var attributeType = metaAttribute.Resolve();
                var importedAttribute = method.Module.ImportReference(attributeType);
                var constructor = targetModule.ImportReference(attributeType.GetConstructors().First());
                var onEntryMethod = targetModule.ImportReference(attributeType.Methods.Single(o => o.Name == "OnEntry"));
                var onExitMethod = targetModule.ImportReference(attributeType.Methods.Single(o => o.Name == "OnExit"));

                var il = method.Body.GetILProcessor();
                var firstInstruction = method.Body.Instructions.First();
                var lastInstruction = method.Body.Instructions.Last();

                var attributeVariable = new VariableDefinition(importedAttribute);
                method.Body.Variables.Add(attributeVariable);

                il.InsertBefore(firstInstruction, il.Create(OpCodes.Newobj, constructor));
                il.InsertBefore(firstInstruction, il.Create(OpCodes.Stloc, attributeVariable));
                il.InsertBefore(firstInstruction, il.Create(OpCodes.Ldloc, attributeVariable));
                il.InsertBefore(firstInstruction,
                    method.HasThis ? il.Create(OpCodes.Ldarg_0) : il.Create(OpCodes.Ldnull));
                il.InsertBefore(firstInstruction, il.Create(OpCodes.Ldstr, fullMethodName));

                var parameters = method.Parameters
                    .Where(o => !o.IsOut || !o.ParameterType.IsByReference)
                    .ToArray();

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

                il.InsertBefore(firstInstruction, il.Create(OpCodes.Callvirt, onEntryMethod));

                var onExitInstruction = il.Create(OpCodes.Ldloc, attributeVariable);
                il.InsertBefore(lastInstruction, onExitInstruction);
                il.InsertBefore(lastInstruction,
                    method.HasThis ? il.Create(OpCodes.Ldarg_0) : il.Create(OpCodes.Ldnull));
                il.InsertBefore(lastInstruction, il.Create(OpCodes.Ldstr, fullMethodName));
                il.InsertBefore(lastInstruction, il.Create(OpCodes.Callvirt, onExitMethod));

                ReplaceJumps(method.Body);
            }
        }
    }

    private static bool MethodThatHasMetaAttributeOrContainingTypeHasMetaAttribute(MethodDefinition method)
        => method.CustomAttributes.Any(a => a.AttributeType.Resolve().BaseType.Name == "MetaAttribute") ||
               method.DeclaringType.CustomAttributes.Any(
                   a => a.AttributeType.Resolve().BaseType.Name == "MetaAttribute");

    private Result InjectAttributes(TypeDefinition[] types, InjectionConfig injectionConfig)
    {
        var attributesSet = injectionConfig.Entries
            .Select(o => o.Attribute)
            .ToHashSet();

        var attributes = types
            .Where(o => attributesSet.Contains(o.FullName))
            .ToDictionary(o => o.FullName);

        var entriesByType = injectionConfig.Entries
            .ToDictionary(o => o.Type);

        var typesToProcess = types
            .Where(o => entriesByType.ContainsKey(o.FullName))
            .ToDictionary(o => o.FullName);

        var missingTypes = entriesByType
            .Where(o => !typesToProcess.ContainsKey(o.Key))
            .Select(o => o.Key)
            .ToArray();

        if (missingTypes.Any())
        {
            return missingTypes
                .Select(o => new Error("MISSING_TYPE", $"The type '{o}' is missing"))
                .ToArray();
        }
        
        foreach (var type in typesToProcess.Values)
        {
            if (!entriesByType.TryGetValue(type.FullName, out var injectableEntry))
            {
                continue;
            }

            if (!attributes.TryGetValue(injectableEntry.Attribute, out var injectableAttribute))
            {
                return Result.Error(new Error("FAILED_TO_FIND_ATTRIBUTE", $"Failed to find meta attribute '{injectableEntry.Attribute}'"));
            }

            var moduleAttribute = injectableAttribute;
            var methodDefinition = moduleAttribute.GetConstructors().First();
            var attributeConstructor = type.Module.ImportReference(methodDefinition);
            if (injectableEntry.Methods is null)
            {
                type.CustomAttributes.Add(new CustomAttribute(attributeConstructor));
                continue;
            }

            var injectableMethods = type.Methods
                .IntersectBy(injectableEntry.Methods, o => o.Name)
                .ToDictionary(o => o.Name);

            var missingMethods = injectableEntry.Methods
                .Where(o => !injectableMethods.ContainsKey(o))
                .ToArray();

            if (missingMethods.Any())
            {
                return missingMethods
                    .Select(o => new Error("MISSING_METHOD", $"The method '{o}' is missing in type '{injectableEntry.Type}'"))
                    .ToArray();
            }
            
            foreach (var injectableMethod in injectableMethods.Values)
            {
                injectableMethod.CustomAttributes.Add(new CustomAttribute(attributeConstructor));
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