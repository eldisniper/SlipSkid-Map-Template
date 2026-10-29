using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SlipSkid.MapKit.Editor
{
    // A small JSON reader for map files (JsonMapGenerator): Unity's JsonUtility can't read a list of steps of different
    // shapes. Objects come back as Dictionary<string, object> (key order kept in MapJson.Obj.Order), arrays as
    // List<object>, numbers as double, plus string, bool and null. Errors give the line and column.
    internal static class MapJson
    {
        public sealed class Error : Exception
        {
            public Error(string message) : base(message) { }
        }

        // Keys in file order (Dictionary doesn't promise one), for error messages and "one action per step" checks.
        public sealed class Obj : Dictionary<string, object>
        {
            public readonly List<string> Order = new List<string>();
        }

        public static object Parse(string text)
        {
            var p = new Parser(text);
            p.Skip();
            object value = p.Value();
            p.Skip();
            if (!p.End) throw p.Fail("unexpected text after the end");
            return value;
        }

        private sealed class Parser
        {
            private readonly string _s;
            private int _i;

            public Parser(string s) { _s = s; }

            public bool End => _i >= _s.Length;

            public Error Fail(string what)
            {
                int line = 1, col = 1;
                for (int k = 0; k < _i && k < _s.Length; k++)
                {
                    if (_s[k] == '\n') { line++; col = 1; }
                    else col++;
                }
                return new Error($"JSON line {line}, column {col}: {what}");
            }

            public void Skip()
            {
                while (!End)
                {
                    char c = _s[_i];
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n') _i++;
                    else if (c == '/' && _i + 1 < _s.Length && _s[_i + 1] == '/')   // // comments: handy in hand-written maps
                    {
                        while (!End && _s[_i] != '\n') _i++;
                    }
                    else break;
                }
            }

            public object Value()
            {
                if (End) throw Fail("unexpected end of file");
                char c = _s[_i];
                if (c == '{') return Object();
                if (c == '[') return Array();
                if (c == '"') return String();
                if (c == '-' || (c >= '0' && c <= '9')) return Number();
                if (Word("true")) return true;
                if (Word("false")) return false;
                if (Word("null")) return null;
                throw Fail($"unexpected '{c}'");
            }

            private bool Word(string w)
            {
                if (string.CompareOrdinal(_s, _i, w, 0, w.Length) != 0) return false;
                _i += w.Length;
                return true;
            }

            private Obj Object()
            {
                var o = new Obj();
                _i++;
                Skip();
                if (!End && _s[_i] == '}') { _i++; return o; }
                while (true)
                {
                    Skip();
                    if (End || _s[_i] != '"') throw Fail("expected a \"key\"");
                    string key = String();
                    if (o.ContainsKey(key)) throw Fail($"key \"{key}\" twice");
                    Skip();
                    if (End || _s[_i] != ':') throw Fail($"expected ':' after \"{key}\"");
                    _i++;
                    Skip();
                    o[key] = Value();
                    o.Order.Add(key);
                    Skip();
                    if (End) throw Fail("unexpected end of file in an object");
                    if (_s[_i] == ',') { _i++; continue; }
                    if (_s[_i] == '}') { _i++; return o; }
                    throw Fail("expected ',' or '}'");
                }
            }

            private List<object> Array()
            {
                var a = new List<object>();
                _i++;
                Skip();
                if (!End && _s[_i] == ']') { _i++; return a; }
                while (true)
                {
                    Skip();
                    a.Add(Value());
                    Skip();
                    if (End) throw Fail("unexpected end of file in a list");
                    if (_s[_i] == ',') { _i++; continue; }
                    if (_s[_i] == ']') { _i++; return a; }
                    throw Fail("expected ',' or ']'");
                }
            }

            private string String()
            {
                var sb = new StringBuilder();
                _i++;
                while (true)
                {
                    if (End) throw Fail("unterminated string");
                    char c = _s[_i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (End) throw Fail("unterminated string");
                    char e = _s[_i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_i + 4 > _s.Length) throw Fail("bad \\u escape");
                            sb.Append((char)int.Parse(_s.Substring(_i, 4), NumberStyles.HexNumber));
                            _i += 4;
                            break;
                        default: throw Fail($"bad escape \\{e}");
                    }
                }
            }

            private double Number()
            {
                int start = _i;
                if (_s[_i] == '-') _i++;
                while (!End && "0123456789.eE+-".IndexOf(_s[_i]) >= 0) _i++;
                string n = _s.Substring(start, _i - start);
                if (!double.TryParse(n, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                {
                    _i = start;
                    throw Fail($"bad number '{n}'");
                }
                return d;
            }
        }
    }
}
