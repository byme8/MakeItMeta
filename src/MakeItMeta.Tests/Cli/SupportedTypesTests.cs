namespace MakeItMeta.Tests.Cli;

[UsesVerify]
public class SupportedTypesTests : InjectionTest
{
    [Fact]
    public async Task OverrideAndVirtual()
    {
        var newFile = """
            public class ExecutorVirtual
            {
                public virtual object? Execute()
                {
                    return null; // place to replace
                }
            }

            public class ExecutorOverride : ExecutorVirtual
            {
                public override object? Execute()
                {
                    return null; // place to replace
                }
            }
            """;
        var replace = "return new Provider().Provide().Execute(); // place to replace";
        var main = """
                    new ExecutorVirtual().Execute();
                    return new ExecutorOverride().Execute();
        """;

        await Execute(newFile, Config, (replace, main));
    }

    [Fact]
    public async Task MultipleReturnsWith1()
    {
        var newFile = """ 
            public struct MultiplrReturns 
            {
                public int DoIt(int value)
                {
                    var result = 0;
                    if(value == 2)
                    {
                        result = 20;
                        return result;
                    }

                    return result + value;
                }
            }
            """;
        var replace = "return new Provider().Provide().Execute(); // place to replace";
        var main = $"return new MultiplrReturns().DoIt(1);";

        await Execute(newFile, Config, (replace, main));
    }

    [Fact]
    public async Task MultipleReturnsWith2()
    {
        var newFile = """ 
            public struct MultiplrReturns 
            {
                public int DoIt(int value)
                {
                    var result = 0;
                    if(value == 2)
                    {
                        result = 20;
                        return result;
                    }

                    return result + value;
                }
            }
            """;
        var replace = "return new Provider().Provide().Execute(); // place to replace";
        var main = $"return new MultiplrReturns().DoIt(2);";

        await Execute(newFile, Config, (replace, main));
    }

    [Fact]
    public async Task Struct()
    {
        var newFile = """
            public struct Color 
            {
                public int R { get; set; }
                public int G { get; set; }
                public int B { get; set; }
                public int A { get; set; }

                public uint ToUint32()
                {
                    return ((uint)A << 24) | ((uint)R << 16) | ((uint)G << 8) | (uint)B;
                }
            }

            """;
        var replace = "return new Provider().Provide().Execute(); // place to replace";
        var main = "return new Color().ToUint32();";

        await Execute(newFile, Config, (replace, main));
    }

    [Fact]
    public async Task MethodWithLock()
    {
        var newFile = """
            using System;
            using System.Collections.Generic;
            
            public class Container
            {
                private List<string> _items = new List<string>();

                public void Add(string text)
                {
                    _ = text ?? throw new ArgumentNullException(nameof(text));
                    lock (_items)
                    {
                        _items.Add(text);
                    }
                }
            }

            """;
        var replace = "return new Provider().Provide().Execute(); // place to replace";
        var main = @"new Container().Add(""hello""); return 1;";

        await Execute(newFile, Config, (replace, main));
    }

    [Fact]
    public async Task VoidReturn()
    {
        var newFile = """
            using System;
            using System.Collections.Generic;
            
            public class Container
            {
                private List<string> _items = new List<string>();

                public void Add(string text)
                {
                    _ = text ?? throw new ArgumentNullException(nameof(text));
                    _items.Add(text);
                }
            }

            """;
        var replace = "return new Provider().Provide().Execute(); // place to replace";
        var main = @"new Container().Add(""hello""); return 1;";

        await Execute(newFile, Config, (replace, main));
    }

    [Fact]
    public async Task GenericStruct()
    {
        var newFile = """
            using System;
            using System.Collections.Generic;
            
            public readonly struct Optional<T> : IEquatable<Optional<T>>
            {
                private readonly T _value;

                public Optional(T value)
                {
                    _value = value;
                    HasValue = true;
                }

                public bool HasValue { get; }

                public T Value => HasValue ? _value : throw new InvalidOperationException("Optional has no value.");
                
                public override bool Equals(object? obj) => obj is Optional<T> o && this == o; 
                
                public bool Equals(Optional<T> other) => this == other; 
                
                public static bool operator !=(Optional<T> x, Optional<T> y) => !(x == y);
                
                public static bool operator ==(Optional<T> x, Optional<T> y)
                {
                    if (!x.HasValue && !y.HasValue)
                    {
                        return true;
                    }
                    else if (x.HasValue && y.HasValue)
                    {
                        return EqualityComparer<T>.Default.Equals(x.Value, y.Value);
                    }
                    else
                    {
                        return false;
                    }
                }
            }

            """;
        var replace = "return new Provider().Provide().Execute(); // place to replace";
        var main = "return new Optional<int>(123).HasValue;";

        await Execute(newFile, Config, (replace, main));
    }

    [Fact]
    public async Task Generics()
    {
        var newFile = """
            public class Container<TValue>
            {
                public TValue Get()
                {
                    return default;
                }

                public TAsValue GetAs<TAsValue>()
                {
                    return default;
                }

                public TValue Execute()
                {
                    GetAs<string>();
                    return Get();
                }
            }

            """;
        var replace = "return new Provider().Provide().Execute(); // place to replace";
        var main = """
            return new Container<int>().Execute();
            """;

        await Execute(newFile, Config, (replace, main));
    }
    
    [Fact]
    public async Task OutAndRefParameters()
    {
        var newFile = """
            public class Container<TValue>
            {
                TValue _value;
                
                public Container(TValue value)
                {
                    _value = value;
                }
            
                public bool TryGet(out TValue value)
                {
                    value = _value;
                    return true;
                }

                public bool TryPopulate(ref TValue value)
                {
                    value = _value;
                    return true;
                }
                
                public bool SetAndRaise<T>(List<T> property, ref T field, T value)
                {
                    if (EqualityComparer<T>.Default.Equals(field, value))
                    {
                        return false;
                    }

                    var old = field;
                    field = value;
                    return true;
                }

                public TValue Execute()
                {
                    TryGet(out var newValue);
                    TryPopulate(ref newValue);
                    
                    int a = 10;
                    SetAndRaise<int>(new List<int>(), ref a, 42);
                    
                    return newValue;
                }
            }

            """;
        var replace = "return new Provider().Provide().Execute(); // place to replace";
        var main = """
            return new Container<int>(42).Execute();
            """;

        await Execute(newFile, Config, (replace, main));
    }
    
    
    [Fact]
    public async Task ByRefLikeStructsAreIgnore()
    {
        var newFile = """
            public ref struct RefLikeStruct
            {
                public int Get()
                {
                    return 42;
                }
            }
        
            public class Container
            {
                public bool TryGet(Span<byte> bytes)
                {
                    return true;
                }

                public bool Execute()
                {
                    var a = new RefLikeStruct();
                    var value = a.Get(); 
                    return TryGet(new byte[0]);
                }
            }

            """;
        var replace = "return new Provider().Provide().Execute(); // place to replace";
        var main = """
            return new Container().Execute();
            """;

        await Execute(newFile, Config, (replace, main));
    }
    
    [Fact]
    public async Task AsyncAwait()
    {
        var newFile = """
            public class Container
            {
                public async Task<string> Get()
                {
                    await Task.Delay(10);
                    return "hello";
                }

                public string Execute()
                {
                    return Get().Result;
                }
            }

            """;
        var replace = "return new Provider().Provide().Execute(); // place to replace";
        var main = "return new Container().Execute();";

        await Execute(newFile, Config, (replace, main));
    }
}