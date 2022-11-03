using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace MakeItMeta.Core;

public class MetaMaker
{
    public Stream MakeItMeta(Stream assembly)
    {
        var module = ModuleDefinition.ReadModule(assembly);

        var types = module.Types
            .ToArray();

        var methods = types
            .SelectMany(o => o.Methods)
            .Where(o => o.HasBody)
            .Where(o => o.CustomAttributes.Any(a => a.AttributeType.Resolve().BaseType.Name == "MetaAttribute") ||
                        o.DeclaringType.CustomAttributes.Any(
                            a => a.AttributeType.Resolve().BaseType.Name == "MetaAttribute"))
            .ToArray();

        foreach (var method in methods)
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
                var onEntryMethod = attributeType.Methods.Single(o => o.Name == "OnEntry");
                var onExitMethod = attributeType.Methods.Single(o => o.Name == "OnExit");

                var il = method.Body.GetILProcessor();
                var firstInstruction = method.Body.Instructions.First();
                var lastInstruction = method.Body.Instructions.Last();

                var attributeVariable = new VariableDefinition(attributeType);
                method.Body.Variables.Add(attributeVariable);

                il.InsertBefore(firstInstruction, il.Create(OpCodes.Newobj, attributeType.GetConstructors().First()));
                il.InsertBefore(firstInstruction, il.Create(OpCodes.Stloc, attributeVariable));
                il.InsertBefore(firstInstruction, il.Create(OpCodes.Ldloc, attributeVariable));
                il.InsertBefore(firstInstruction,
                    method.HasThis ? il.Create(OpCodes.Ldarg_0) : il.Create(OpCodes.Ldnull));
                il.InsertBefore(firstInstruction, il.Create(OpCodes.Ldstr, fullMethodName));

                var parameters = method.Parameters
                    .Where(o => !o.IsOut || !o.ParameterType.IsByReference)
                    .ToArray();

                il.InsertBefore(firstInstruction, il.Create(OpCodes.Ldc_I4, parameters.Length));
                il.InsertBefore(firstInstruction, il.Create(OpCodes.Newarr, module.ImportReference(typeof(object))));
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

        var memoryStream = new MemoryStream();
        module.Write(memoryStream);

        return memoryStream;
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