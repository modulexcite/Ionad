﻿using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

public class ModuleWeaver
{
    public Action<string> LogInfo { get; set; }
    public Action<string> LogError { get; set; }
    public ModuleDefinition ModuleDefinition { get; set; }
    public IAssemblyResolver AssemblyResolver { get; set; }
    public string[] DefineConstants { get; set; }

    public ModuleWeaver()
    {
        LogInfo = s => { };
        LogError = s => { };
        DefineConstants = new string[0];
    }

    public void Execute()
    {
        var types = ModuleDefinition.GetTypes();
        var replacements = FindReplacements(types);

        if (replacements.Count == 0)
            LogInfo("No Static Replacements found");
        else
        {
            ProcessAssembly(types, replacements);
            RemoveAttributes(replacements.Values);
        }

        RemoveReference();
    }

    private Dictionary<TypeDefinition, TypeDefinition> FindReplacements(IEnumerable<TypeDefinition> types)
    {
        var replacements = new Dictionary<TypeDefinition, TypeDefinition>();

        foreach (var type in types)
        {
            var replacement = type.GetStaticReplacementAttribute();
            if (replacement == null)
                continue;

            var replacementType = ((TypeReference)replacement.ConstructorArguments[0].Value).Resolve();

            replacements.Add(replacementType, type);
        }

        return replacements;
    }

    private void ProcessAssembly(IEnumerable<TypeDefinition> types, Dictionary<TypeDefinition, TypeDefinition> replacements)
    {
        foreach (var type in types)
        {
            foreach (var method in type.MethodsWithBody())
                ReplaceCalls(method.Body, replacements);

            foreach (var property in type.ConcreteProperties())
            {
                if (property.GetMethod != null)
                    ReplaceCalls(property.GetMethod.Body, replacements);
                if (property.SetMethod != null)
                    ReplaceCalls(property.SetMethod.Body, replacements);
            }
        }
    }

    private void ReplaceCalls(MethodBody body, Dictionary<TypeDefinition, TypeDefinition> replacements)
    {
        body.SimplifyMacros();

        var calls = body.Instructions.Where(i => i.OpCode == OpCodes.Call);

        foreach (var call in calls)
        {
            var method = ((MethodReference)call.Operand).Resolve();
            var declaringType = method.DeclaringType.Resolve();

            if (!method.IsStatic || !replacements.ContainsKey(declaringType))
                continue;

            var replacement = replacements[declaringType];
            var replacementMethod = replacement.Methods.FirstOrDefault(m => m.Name == method.Name);

            if (replacementMethod == null)
            {
                LogError(String.Format("Missing '{0}.{1}()' in '{2}'", declaringType.FullName, method.Name, replacement.FullName));
                continue;
            }

            call.Operand = replacementMethod;
        }

        body.InitLocals = true;
        body.OptimizeMacros();
    }

    private void RemoveAttributes(IEnumerable<TypeDefinition> types)
    {
        foreach (var typeDefinition in types)
            typeDefinition.RemoveStaticReplacementAttribute();
    }

    private void RemoveReference()
    {
        var referenceToRemove = ModuleDefinition.AssemblyReferences.FirstOrDefault(x => x.Name == "Ionad");
        if (referenceToRemove == null)
        {
            LogInfo("\tNo reference to 'Ionad.dll' found. References not modified.");
            return;
        }

        ModuleDefinition.AssemblyReferences.Remove(referenceToRemove);
        LogInfo("\tRemoving reference to 'Ionad.dll'.");
    }
}