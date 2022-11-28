using System.Reflection;
using MakeItMeta.Attributes;
namespace MakeItMeta.TestAttributes
{
    public class TestAttribute : MetaAttribute
    {
        public static Dictionary<string, List<Entry>> MethodsByAssembly { get; set; } = new Dictionary<string, List<Entry>>();

        public static Entry? OnEntry(object? @this, string assemblyFullName, string methodFullName, object[]? parameters)
        {
            var assemblyThatCalls = Assembly.GetCallingAssembly().FullName;
            if (!MethodsByAssembly.ContainsKey(assemblyThatCalls))
            {
                MethodsByAssembly.Add(assemblyThatCalls, new List<Entry>());
            }

            var entry = new Entry
            {
                Kind = "OnEntry",
                This = @this,
                AssemblyFullname = assemblyFullName,
                MethodFullName = methodFullName,
                Parameters = parameters
            };

            MethodsByAssembly[assemblyThatCalls].Add(entry);

            return entry;
        }

        public static void OnExit(object? @this, string assemblyFullName, string methodFullName, object[]? parameters, Entry? entry)
        {
            var assemblyThatCalls = Assembly.GetCallingAssembly().FullName;
            if (!MethodsByAssembly.ContainsKey(assemblyThatCalls))
            {
                MethodsByAssembly.Add(assemblyThatCalls, new List<Entry>());
            }

            var exitEntry = new Entry()
            {
                Kind = "OnExit",
                This = @this,
                AssemblyFullname = assemblyFullName,
                MethodFullName = methodFullName,
                Parameters = parameters
            };

            MethodsByAssembly[assemblyThatCalls].Add(exitEntry);
        }
    }

    public class Entry
    {
        public string Kind { get; set; }
        
        public object? This
        {
            get;
            set;
        }

        public string AssemblyFullname
        {
            get;
            set;
        }

        public string MethodFullName
        {
            get;
            set;
        }

        public object[]? Parameters
        {
            get;
            set;
        }
    }
}