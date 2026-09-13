using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CityLife.World
{
    // Small bounded JSON codec. Duplicate keys, trailing data and excessive nesting are errors.
    // No dynamic type names, reflection, code evaluation, comments or permissive repair.
    public static class NpcBoundedJson
    {
        public static object Parse(string json, int maxBytes = 4096)
        {
            if (json == null || Encoding.UTF8.GetByteCount(json) > maxBytes) throw new FormatException("json-size");
            var reader = new Reader(json); object value = reader.Value(0); reader.Space();
            if (reader.Position != json.Length) throw new FormatException("json-trailing-data");
            return value;
        }
        public static string Encode(object value)
        {
            if (value == null) return "null";
            if (value is string s)
            {
                var b = new StringBuilder("\"");
                foreach (char c in s)
                {
                    if (c == '"' || c == '\\') b.Append('\\').Append(c);
                    else if (c < 32) b.Append("\\u").Append(((int)c).ToString("x4"));
                    else b.Append(c);
                }
                return b.Append('"').ToString();
            }
            if (value is bool flag) return flag ? "true" : "false";
            if (value is IDictionary<string, object> map)
            {
                var parts = new List<string>();
                foreach (var item in map) parts.Add(Encode(item.Key) + ":" + Encode(item.Value));
                return "{" + string.Join(",", parts) + "}";
            }
            if (value is IEnumerable sequence)
            {
                var parts = new List<string>(); foreach (object item in sequence) parts.Add(Encode(item));
                return "[" + string.Join(",", parts) + "]";
            }
            if (value is int || value is long || value is double) return Convert.ToString(value, CultureInfo.InvariantCulture);
            throw new ArgumentException("Unsupported JSON value");
        }
        private sealed class Reader
        {
            private readonly string input;
            public int Position;
            public Reader(string input) { this.input = input; }
            public void Space() { while (Position < input.Length && " \t\n\r".IndexOf(input[Position]) >= 0) Position++; }
            private bool Eat(char c) { Space(); if (Position >= input.Length || input[Position] != c) return false; Position++; return true; }
            private void Require(char c) { if (!Eat(c)) throw new FormatException("json-token"); }
            public object Value(int depth)
            {
                if (depth > 10) throw new FormatException("json-depth"); Space();
                if (Position >= input.Length) throw new FormatException("json-incomplete");
                if (input[Position] == '"') return String();
                if (Eat('{'))
                {
                    var map = new Dictionary<string, object>(StringComparer.Ordinal);
                    if (Eat('}')) return map;
                    do
                    {
                        string key = String(); Require(':');
                        if (map.Count >= 64 || map.ContainsKey(key)) throw new FormatException("json-duplicate-or-many-fields");
                        map.Add(key, Value(depth + 1));
                    } while (Eat(','));
                    Require('}'); return map;
                }
                if (Eat('['))
                {
                    var list = new List<object>(); if (Eat(']')) return list;
                    do { if (list.Count >= 128) throw new FormatException("json-array-size"); list.Add(Value(depth + 1)); } while (Eat(','));
                    Require(']'); return list;
                }
                foreach (string literal in new[] { "true", "false", "null" })
                    if (Position + literal.Length <= input.Length && string.CompareOrdinal(input, Position, literal, 0, literal.Length) == 0)
                    { Position += literal.Length; return literal == "null" ? null : (object)(literal == "true"); }
                int start = Position;
                if (input[Position] == '-') Position++;
                if (Position >= input.Length || input[Position] < '0' || input[Position] > '9') throw new FormatException("json-number");
                if (input[Position] == '0') Position++;
                else while (Position < input.Length && input[Position] >= '0' && input[Position] <= '9') Position++;
                bool integer = true;
                if (Position < input.Length && input[Position] == '.')
                { integer = false; Position++; Digits(); }
                if (Position < input.Length && (input[Position] == 'e' || input[Position] == 'E'))
                { integer = false; Position++; if (Position < input.Length && (input[Position] == '+' || input[Position] == '-')) Position++; Digits(); }
                string number = input.Substring(start, Position - start);
                if (integer && long.TryParse(number, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long n)) return n;
                if (!integer && double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && !double.IsInfinity(d) && !double.IsNaN(d)) return d;
                throw new FormatException("json-number-range");
            }
            private void Digits()
            {
                int start = Position;
                while (Position < input.Length && input[Position] >= '0' && input[Position] <= '9') Position++;
                if (start == Position) throw new FormatException("json-number-digits");
            }
            private string String()
            {
                Require('"'); var text = new StringBuilder();
                while (Position < input.Length)
                {
                    char c = input[Position++]; if (c == '"') return text.ToString();
                    if (c < 32 || text.Length >= 8192) throw new FormatException("json-string");
                    if (c == '\\')
                    {
                        if (Position >= input.Length) throw new FormatException("json-escape");
                        c = input[Position++];
                        switch (c)
                        {
                            case '"': case '\\': case '/': break;
                            case 'b': c = '\b'; break; case 'f': c = '\f'; break;
                            case 'n': c = '\n'; break; case 'r': c = '\r'; break; case 't': c = '\t'; break;
                            case 'u':
                                if (Position + 4 > input.Length || !ushort.TryParse(input.Substring(Position, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort code)) throw new FormatException("json-unicode");
                                Position += 4; c = (char)code; break;
                            default: throw new FormatException("json-escape");
                        }
                    }
                    text.Append(c);
                }
                throw new FormatException("json-string-incomplete");
            }
        }
    }
}
