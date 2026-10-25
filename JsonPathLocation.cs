using System;
using System.Globalization;

namespace JsonPathPlus;

internal sealed class JsonPathLocation
{
  public static readonly JsonPathLocation Root = new();
  private readonly JsonPathLocation? _parent;
  private readonly string? _property;
  private readonly int _index;
  private readonly bool _simple;
  private readonly int _length = 1;

  private JsonPathLocation() { }

  public JsonPathLocation(JsonPathLocation parent, string property)
  {
    _parent = parent;
    _property = property;
    _simple = JsonPathMatcher.IsSimpleIdentifier(property);
    _length = checked(parent._length + PropertyLength(property, _simple));
  }

  public JsonPathLocation(JsonPathLocation parent, int index)
  {
    _parent = parent;
    _index = index;
    var digits = index < 0 ? 1 : 0;
    var number = Math.Abs((long)index);
    do
    {
      digits++;
      number /= 10;
    } while (number > 0);
    _length = checked(parent._length + digits + 2);
  }

  public override string ToString()
    => string.Create(_length, this, static (text, location) =>
    {
      for (var current = location; current._parent is not null; current = current._parent)
      {
        var segment = text[current._parent._length..current._length];
        if (current._property is not null)
          WriteProperty(segment, current._property, current._simple);
        else
        {
          segment[0] = '[';
          current._index.TryFormat(segment[1..^1], out _, provider: CultureInfo.InvariantCulture);
          segment[^1] = ']';
        }
      }
      text[0] = '$';
    });

  internal static string AppendProperty(string parent, string property)
  {
    var simple = JsonPathMatcher.IsSimpleIdentifier(property);
    return string.Create(checked(parent.Length + PropertyLength(property, simple)),
      (parent, property, simple), static (text, state) =>
      {
        state.parent.AsSpan().CopyTo(text);
        WriteProperty(text[state.parent.Length..], state.property, state.simple);
      });
  }

  private static int PropertyLength(string property, bool simple)
  {
    if (simple)
      return property.Length + 1;
    var length = property.Length + 4;
    foreach (var ch in property)
    {
      if (ch is '\\' or '\'')
        length++;
    }
    return length;
  }

  private static void WriteProperty(Span<char> text, string property, bool simple)
  {
    if (simple)
    {
      text[0] = '.';
      property.AsSpan().CopyTo(text[1..]);
      return;
    }
    text[0] = '[';
    text[1] = '\'';
    var index = 2;
    foreach (var ch in property)
    {
      if (ch is '\\' or '\'')
        text[index++] = '\\';
      text[index++] = ch;
    }
    text[index++] = '\'';
    text[index] = ']';
  }
}
