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

        return newAssembly;
    }

    private Result InjectInterceptorBaseOnAttributes(TypeDefinition[] types, ModuleDefinition targetModule)
    {
        var methodsWithMetaAttributes = types
            .SelectMany(o => o.Methods)
            .Where(o => o.HasBody)
            .Where(MethodThatHasMetaAttributeOrContainingTypeHasMetaAttribute)
            .ToArray();

        foreach (var method in methodsWithMetaAttributes)
        {
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
                    o => o.IsOut || o.ParameterType.IsByReference || o.ParameterType.IsGenericInstance))
            {
                var message = $"At the moment method '{fullMethodName}' is not supported. It has generics or by references parameters";
                return new Error("METHOD_IS_NOT_SUPPORTED", message);
            }

            var newMethodName = $"<>InternalMetaCopy{method.Name}";
            var copyMethod = CopyMethodWithNewName(newMethodName,method);
            
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
                var parameters = method.Parameters
                    .Where(o => !o.IsOut || !o.ParameterType.IsByReference)
                    .ToArray();
                
                method.Body.Instructions.Clear();
                var il = method.Body.GetILProcessor();
                il.Append(il.Create(OpCodes.Ldarg_0));
                foreach (var parameter in parameters)
                {
                    il.Append(il.Create(OpCodes.Ldarg, parameter));
                }
                
                il.Append(il.Create(OpCodes.Call, copyMethod));
                il.Append(il.Create(OpCodes.Ret));
                
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
            }
        }

        return Result.Success();
    }

    private MethodDefinition CopyMethodWithNewName(string newMethodName, MethodDefinition method)
    {
        var copyMethod = new MethodDefinition(newMethodName, method.Attributes, method.ReturnType);
        foreach (var parameter in method.Parameters)
        {
            copyMethod.Parameters.Add(new ParameterDefinition(parameter.Name, parameter.Attributes, parameter.ParameterType));
        }
        
        foreach (var variable in method.Body.Variables)
        {
            copyMethod.Body.Variables.Add(new VariableDefinition(variable.VariableType));
        }
        
        foreach (var instruction in method.Body.Instructions)
        {
            copyMethod.Body.Instructions.Add(instruction);
        }
        
        foreach (var exceptionHandler in method.Body.ExceptionHandlers)
        {
            copyMethod.Body.ExceptionHandlers.Add(exceptionHandler);
        }
        
        method.DeclaringType.Methods.Add(copyMethod);
        return copyMethod;
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
}