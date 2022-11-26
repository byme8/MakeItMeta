using MakeItMeta.Tools.Results;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace MakeItMeta.Tools;

public class MetaMaker
{
    public Result<IReadOnlyList<MemoryStream>> MakeItMeta(Stream[] targetAssemblies, InjectionConfig? injectionConfig = null, string[]? searchFolders = null)
    {
        var readParameters = PrepareReadParameters(searchFolders);
        var targetModules = targetAssemblies
            .Select(o => ModuleDefinition.ReadModule(o, readParameters))
            .ToArray();

        var injectableModules = injectionConfig?
            .AdditionalAssemblies?
            .Select(ModuleDefinition.ReadModule)
            .ToArray() ?? Array.Empty<ModuleDefinition>();

        var allModules = new List<ModuleDefinition>();
        allModules.AddRange(targetModules);
        allModules.AddRange(injectableModules);

        var allTypes = allModules
            .SelectMany(o => o.Types)
            .ToArray();

        var validationError = MetaValidator.ValidateConfig(allTypes, injectionConfig).Unwrap();
        if (validationError)
        {
            return validationError;
        }

        var resultAssemblies = new List<MemoryStream>(targetAssemblies.Length);
        foreach (var targetModule in targetModules)
        {
            if (injectionConfig is not null)
            {
                var injectAttributeError = InjectAttributes(targetModule, allTypes, injectionConfig).Unwrap();
                if (injectAttributeError)
                {
                    return injectAttributeError;
                }
            }

            var injectionError = InjectInterceptorBaseOnAttributes(allTypes, targetModule).Unwrap();
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

    private ReaderParameters PrepareReadParameters(string[]? searchFolders)
    {
        var resolver = new Mono.Cecil.DefaultAssemblyResolver();
        if (searchFolders is not null)
        {
            foreach (var folder in searchFolders)
            {
                resolver.AddSearchDirectory(folder);
            }
        }
        var readParameters = new ReaderParameters
        {
            AssemblyResolver = resolver
        };

        return readParameters;
    }

    private Result InjectInterceptorBaseOnAttributes(TypeDefinition[] allTypes, ModuleDefinition targetModule)
    {
        var methodsWithMetaAttributes = targetModule.Types
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
                System.Console.WriteLine(message);
                continue;
                // return new Error("METHOD_IS_NOT_SUPPORTED", message);
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

            var attributeValidationError = MetaValidator.ValidateAttributes(metaAttributes).Unwrap();
            if (attributeValidationError)
            {
                return attributeValidationError;
            }

            foreach (var metaAttribute in metaAttributes)
            {
                var attributeType = metaAttribute.AttributeType.Resolve();
                var onEntryMethod = targetModule.ImportReference(attributeType.Methods.Single(o => o.Name == "OnEntry"));
                var onExitMethod = targetModule.ImportReference(attributeType.Methods.Single(o => o.Name == "OnExit"));
                var onEntryReturnType = method.Module.ImportReference(onEntryMethod.ReturnType);
                var onEnterVoidReturn = onEntryReturnType.FullName == "System.Void";
                if (!onEnterVoidReturn)
                {
                    var onExitAcceptType = method.Module.ImportReference(onExitMethod.Parameters.Last().ParameterType);
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

                TypeReference declaringType = method.DeclaringType;
                if (declaringType.GenericParameters.Any())
                {
                    var genericsArguments = declaringType.GenericParameters.OfType<TypeReference>().ToArray();
                    declaringType = declaringType
                        .MakeGenericInstanceType(genericsArguments);
                }
                if (method.HasThis)
                {
                    il.InsertBefore(firstInstruction, il.Create(OpCodes.Ldarg_0));
                    
                    if (declaringType.IsValueType)
                    {
                        il.InsertBefore(firstInstruction, il.Create(OpCodes.Ldobj, declaringType));
                        il.InsertBefore(firstInstruction, il.Create(OpCodes.Box, declaringType));
                    }
                }
                else
                {
                    il.InsertBefore(firstInstruction, il.Create(OpCodes.Ldnull));
                }

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

                if (method.HasThis)
                {
                    il.InsertBefore(lastInstruction, il.Create(OpCodes.Ldarg_0));
                    if (declaringType.IsValueType)
                    {
                        il.InsertBefore(lastInstruction, il.Create(OpCodes.Ldobj, declaringType));
                        il.InsertBefore(lastInstruction, il.Create(OpCodes.Box, declaringType));
                    }
                }
                else
                {
                    il.InsertBefore(lastInstruction, il.Create(OpCodes.Ldnull));
                }
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

    private static bool MethodThatHasMetaAttributeOrContainingTypeHasMetaAttribute(MethodDefinition method)
        => method.CustomAttributes.Any(a => method.Module.ImportReference(a.AttributeType).Resolve().BaseType.Name == "MetaAttribute") ||
           method.DeclaringType.CustomAttributes.Any(
               a => method.Module.ImportReference(a.AttributeType).Resolve().BaseType.Name == "MetaAttribute");

    private Result InjectAttributes(ModuleDefinition targetModule, TypeDefinition[] allTypes, InjectionConfig injectionConfig)
    {
        if (injectionConfig.Entries is null)
        {
            return Result.Success();
        }

        var attributesSet = injectionConfig.Entries
            .Select(o => o.Attribute)
            .ToHashSet();

        var metaAttributes = allTypes
            .Where(o => attributesSet.Contains(o.FullName))
            .ToDictionary(o => o.FullName);

        var allMethods = targetModule.Types
            .Where(o => !o.IsInterface)
            .Where(o => !o.FullName.StartsWith("System."))
            .Where(o => !o.FullName.StartsWith("Microsoft."))
            .Where(o => !metaAttributes.ContainsKey(o.FullName))
            .SelectMany(o => o.Methods)
            .ToArray();

        var allMethodsByType = allMethods
            .GroupBy(o => o.DeclaringType.FullName)
            .ToDictionary(o => o.Key, o => o.ToArray());

        foreach (var entry in injectionConfig.Entries)
        {
            var attributeTypeDefinition = metaAttributes.GetValueOrDefault(entry.Attribute);
            if (attributeTypeDefinition is null)
            {
                continue;
            }

            var currentMethods = allMethods.ToArray();
            if (entry.Add is not null)
            {
                var methodsToAdd = ExtractMethods(entry.Add, allMethodsByType);
                currentMethods = methodsToAdd;
            }

            if (entry.Ignore is not null)
            {
                var methodsToIgnore = ExtractMethods(entry.Ignore, allMethodsByType);
                var methodToIgnoreSet = methodsToIgnore.ToHashSet();
                currentMethods = currentMethods
                    .Where(o => !methodToIgnoreSet.Contains(o))
                    .ToArray();
            }

            var attributeConstructor = attributeTypeDefinition.GetConstructors().First();
            foreach (var method in currentMethods)
            {
                var typeEntryDefinition = method.DeclaringType;
                var attributeConstructorReference = typeEntryDefinition.Module.ImportReference(attributeConstructor);
                method.CustomAttributes.Add(new CustomAttribute(attributeConstructorReference));
            }

        }

        return Result.Success();
    }

    private static MethodDefinition[] ExtractMethods(InjectionTypeEntry[] entries, Dictionary<string, MethodDefinition[]> allMethodsByType)
    {
        var methods = entries
            .Where(o => allMethodsByType.ContainsKey(o.Name))
            .SelectMany(o =>
            {
                var typeMethods = allMethodsByType[o.Name];
                if (o.Methods?.Any() ?? false)
                {
                    typeMethods = typeMethods
                        .Where(oo => o.Methods.Contains(oo.Name))
                        .ToArray();

                    return typeMethods;
                }

                return typeMethods;
            })
            .ToArray();

        return methods;
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