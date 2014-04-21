namespace JSONParser
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using System.Numerics;

    using Microsoft.Formula.API;
    using Microsoft.Formula.API.ASTQueries;
    using Microsoft.Formula.API.Nodes;
    using Microsoft.Formula.API.Plugins;
    using Microsoft.Formula.Compiler;
    using Microsoft.Formula.Common;

    public class JSONParser : IQuoteParser
    {

        private enum ParserState {
            Object, Members, Pair,
            Array, Elements,
            Str, Val,
            Number, Integer, Frac, Exp
        };

        private enum Token {
            None, Unk,
            LSBrk, RSBrk, //square brackets
            LCBrk, RCBrk, //curly brackets
            Colon, Comma,
            Quote, Backslash, Digit,
            True, False, Null,
            EID //escaped ID
        };


        private static readonly Tuple<string, CnstKind>[] noSettings = new Tuple<string, CnstKind>[0];
        private static readonly AST<Domain> JSONDomain; //TODO init JSONDomain

        private const char idPrefix = '$';
        private const string idPrefixStr = "$";
        private const string Null = "NULL";
        private const string True = "TRUE";
        private const string False = "FALSE";
        private const string EmptyArray = "EMPTY_ARRAY";
        private const string Elements = "Elements";
        private const string EmptyObj = "EMPTY_OBJ";
        private const string Pair = "Pair";
        private const string Members = "Members";

        public AST<Domain> SuggestedDataModel {
            get { return JSONDomain; }
        }

        public IEnumerable<Tuple<string, CnstKind>> SuggestedSettings
        {
            get { return noSettings; }
        }

        public string UnquotePrefix
        {
            get { return idPrefixStr; }
        }

        public JSONParser()
        {

        }

        public IQuoteParser CreateInstance(AST<Node> module, string collectionName, string instanceName)
        {
            return new JSONParser();
        }

        private void consumeWS(PositionalStreamReader reader)
        {
            var c = reader.Peek();
            if (c >= 0)
            {
                var cc = Convert.ToChar(c);
                if (Char.IsWhiteSpace(cc))
                {
                    reader.Read();
                    consumeWS(reader);
                }
            }
        }

        private Span consumeLiteral(String lit, PositionalStreamReader reader)
        {
            var pos1 = reader.getPos();
            var litArray = lit.ToCharArray();
            for (var i = 0; i < lit.Length; i++)
            {
                var c = Convert.ToChar(reader.Read());
                if (c != litArray[i])
                {
                    throw new FormatException("parsing " + lit + " found " + c + " instead of " + litArray[i]);
                }
            }
            var pos2 = reader.getPos();
            return reader.pos().GetSourcePosition(pos1.Item1, pos1.Item2, pos2.Item1, pos2.Item2);
        }

        private Token nextToken(PositionalStreamReader reader)
        {
            consumeWS(reader);
            var c = reader.Peek();
            if (c >= 0)
            {
                var cc = Convert.ToChar(c);
                //Console.WriteLine("nextToken: " + cc);
                if (cc == '"') return Token.Quote;
                else if (cc == '-' || ('0' <= cc && cc <= '9')) return Token.Digit;
                else if (cc == '{') return Token.LCBrk;
                else if (cc == '[') return Token.LSBrk;
                else if (cc == '}') return Token.RCBrk;
                else if (cc == ']') return Token.RSBrk;
                else if (cc == ',') return Token.Comma;
                else if (cc == ':') return Token.Colon;
                else if (cc == 't') return Token.True;
                else if (cc == 'f') return Token.False;
                else if (cc == 'n') return Token.Null;
                else if (cc == idPrefix) return Token.EID;
                else return Token.Unk;
            }
            else
            {
                return Token.None;
            }
        }

        private string escapedChar(PositionalStreamReader reader)
        {
            var c = Convert.ToChar(reader.Read());
            if (c == 'f') { return "\f"; }
            else if (c == 'n') { return "\n"; }
            else if (c == 'b') { return "\b"; }
            else if (c == 'r') { return "\r"; }
            else if (c == 't') { return "\t"; }
            else if (c == '\\') { return "\\"; }
            else if (c == '"')  { return "\""; }
            else if (c == 'u')
            {
                var buffer = new char[4];
                var result = reader.Read(buffer, 4);
                if (result == 4)
                {
                    //todo some more format check ?
                    int value = Convert.ToInt32(new String(buffer), 16);
                    string stringValue = Char.ConvertFromUtf32(value);
                    return stringValue;
                }
                else
                {
                    throw new FormatException("invalid escaped char: \\uXXXX");
                }
            }
            else
            {
                throw new FormatException("invalid escaped char");
            }
        }

        private AST<Node> parseString(PositionalStreamReader reader)
        {
            //Console.WriteLine("parseString");
            consumeWS(reader);
            var builder = new StringBuilder();
            var pos1 = reader.getPos();
            var c = reader.Read();
            if (c >= 0)
            {
                char cc = Convert.ToChar(c);
                if (cc != '"')
                {
                    throw new FormatException("string not starting with \""); ;
                }
            }
            c = reader.Read();
            while (c >= 0)
            {
                char cc = Convert.ToChar(c);
                if(cc == '"') {
                    var pos2 = reader.getPos();
                    var span = reader.pos().GetSourcePosition(pos1.Item1, pos1.Item2, pos2.Item1, pos2.Item2);
                    var res = builder.ToString();
                    return Factory.Instance.MkCnst(res, span);
                }
                else if (cc == '\\')
                {
                    builder.Append(escapedChar(reader));
                }
                else
                {
                    builder.Append(cc);
                }
                c = reader.Read();
            }           
            throw new FormatException("string not ending with \"");
        }

        private bool isNumChar(char c)
        {
            return ('0' <= c && c <= '9')
                || c == '-' || c == '+'
                || c == 'e' || c == 'E'
                || c == '.';
        }

        private AST<Node> parseNum(PositionalStreamReader reader)
        {
            //Console.WriteLine("parseNum");
            consumeWS(reader);
            var pos1 = reader.getPos();
            var builder = new StringBuilder();
            while (true)
            {
                var c = reader.Peek();
                if (c >= 0)
                {
                    char cc = Convert.ToChar(c);
                    if (isNumChar(cc))
                    {
                        reader.Read();
                        builder.Append(cc);
                    }
                    else
                    {
                        break;
                    }
                }
                else
                {
                    break;
                }
            }
            var pos2 = reader.getPos();
            var span = reader.pos().GetSourcePosition(pos1.Item1, pos1.Item2, pos2.Item1, pos2.Item2);
            var res = builder.ToString();
            //Console.WriteLine("num is " + res);
            Contract.Assert(checkNumFormat(res));
            return Factory.Instance.MkCnst(numToRat(res), span);
        }

        private Rational numToRat(String numStr)
        {
            String pattern = "(?<int>-?(?:0|[1-9]\\d*))(?<frac>(?:\\.\\d+)?)(?<exp>(?:[eE][+-]?\\d+)?)";
            var regex = new System.Text.RegularExpressions.Regex(pattern);
            System.Text.RegularExpressions.Match match = regex.Match(numStr);
            var intPart = match.Groups["int"].Value;
            var fracPart = match.Groups["frac"].Value;
            var expPart = match.Groups["exp"].Value;
            var num = BigInteger.Parse(intPart);
            var den = (BigInteger) 1;
            var exp = (BigInteger) 0;
            if (fracPart != "")
            {
                fracPart = fracPart.Substring(1);
                exp = exp - fracPart.Length;
                num = num * exp + BigInteger.Parse(fracPart);
            }
            if (expPart != "")
            {
                expPart = expPart.Substring(1);
                exp = exp + BigInteger.Parse(expPart);
            }
            var ten = (BigInteger) 10;
            if (exp >= 0)
            {
                while(exp > 0) {
                    num = num * ten;
                    exp = exp - 1;
                }
            }
            else
            {
                while (exp < 0)
                {
                    den = den * ten;
                    exp = exp + 1;
                }
            }
            return new Rational(num, den);
        }

        private bool checkNumFormat(String num)
        {
            String pattern = "^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?(?:[eE][+-]?\\d+)?$";
            var regex = new System.Text.RegularExpressions.Regex(pattern);
            return regex.Match(num).Success;
        }

        private AST<Node> escapedID(PositionalStreamReader reader)
        {
            throw new NotImplementedException();
        }

        private AST<Node> parseValue(PositionalStreamReader reader)
        {
            //Console.WriteLine("parseValue");
            var token = nextToken(reader);
            switch (token)
            {
                case Token.LCBrk:
                    return parseObject(reader);
                case Token.LSBrk:
                    return parseArray(reader);
                case Token.Null:
                    return Factory.Instance.MkId(Null, consumeLiteral("null", reader));
                case Token.True:
                    return Factory.Instance.MkId(True, consumeLiteral("true", reader));;
                case Token.False:
                    return Factory.Instance.MkId(False, consumeLiteral("false", reader));;
                case Token.Quote:
                    return parseString(reader);
                case Token.Digit:
                    return parseNum(reader);
                case Token.EID:
                    return escapedID(reader);
                default:
                    throw new FormatException("unable to parse value");
            }
        }

        private AST<Node> parsePair(PositionalStreamReader reader)
        {
            //Console.WriteLine("parsePair");
            var pos1 = reader.getPos();
            var str = parseString(reader);
            var t = nextToken(reader);
            if (t != Token.Colon)
            {
                throw new FormatException("expected ':', found " + t);
            }
            reader.Read();
            var value = parseValue(reader);
            var pos2 = reader.getPos();
            var span = reader.pos().GetSourcePosition(pos1.Item1, pos1.Item2, pos2.Item1, pos2.Item2);
            var pairId = Factory.Instance.MkId(Pair, span);
            var pair = Factory.Instance.MkFuncTerm(pairId, span, new AST<Node>[] { str, value });
            return pair;
        }

        private AST<Node> mkEmpty(String id, PositionalStreamReader reader)
        {
            var pos = reader.getPos();
            var span = reader.pos().GetSourcePosition(pos.Item1, pos.Item2, pos.Item1, pos.Item2);
            return Factory.Instance.MkId(id, span);
        }

        private AST<Node> pack(AST<Node> baseElt, String consName, LinkedList<AST<Node>> revElts)
        {
            AST<Node> results = baseElt;
            foreach (var item in revElts)
            {
                var cons = Factory.Instance.MkId(consName, item.Node.Span);
                results = Factory.Instance.MkFuncTerm(cons, item.Node.Span, new AST<Node>[] { item, results });
            }
            return results;
        }

        private AST<Node> parseArray(PositionalStreamReader reader)
        {
            //Console.WriteLine("parseArray");
            var t = nextToken(reader);
            if (t != Token.LSBrk)
            {
                throw new FormatException("parsing JSON array, expecting '['");
            }
            reader.Read();
            var revElts = new LinkedList<AST<Node>>();
            t = nextToken(reader);
            //this allows arrays ending with ",]"
            while (t != Token.RSBrk)
            {
                revElts.AddFirst(parseValue(reader));
                t = nextToken(reader);
                if (t == Token.Comma)
                {
                    reader.Read();
                }
            }
            reader.Read();//consume the last ']'
            //pack the pairs into an array
            return pack(mkEmpty(EmptyArray, reader), "Elements", revElts);
        }

        private AST<Node> parseObject(PositionalStreamReader reader)
        {
            //Console.WriteLine("parseObject");
            var t = nextToken(reader);
            if (t != Token.LCBrk)
            {
                throw new FormatException("parsing JSON obj, expecting '{'");
            }
            reader.Read();
            var revPairs = new LinkedList<AST<Node>>();
            t = nextToken(reader);
            //this allows objects ending with ",}"
            while (t != Token.RCBrk)
            {
                revPairs.AddFirst(parsePair(reader));
                t = nextToken(reader);
                if (t == Token.Comma) 
                {
                    reader.Read();
                }
            }
            reader.Read();//consume the last '}'
            //pack the pairs into an obj
            return pack(mkEmpty(EmptyObj, reader), "Members", revPairs);
        }
        
        public bool Parse(Configuration config, Stream quoteStream, SourcePositioner positioner, out AST<Node> results, out List<Flag> flags)
        {
            //output variables
            results = null;
            flags = new List<Flag>();
            //to read char/string
            var reader = new PositionalStreamReader(quoteStream, positioner);
            try
            {
                results = parseValue(reader);
                return true;
            }
            catch (Exception e)
            {
                var pos = reader.getPos();
                flags.Add(new Flag(
                    SeverityKind.Error,
                    positioner.GetSourcePosition(pos.Item1, pos.Item2, pos.Item1, pos.Item2),
                    Constants.QuotationError.ToString(e.Message),
                    Constants.QuotationError.Code));
                return false;
            }
        }

        private const int tabStop = 4;
        private const string tab4 = "    ";

        private void fillIndent(TextWriter writer, int indent)
        {
            while (indent >= 4)
            {
                writer.Write(tab4);
                indent -= 4;
            }
            while (indent >= 1)
            {
                writer.Write(" ");
                indent -= 1;
            }
        }

        private LinkedList<Node> collectElts(Node ft)
        {
            var lst = new LinkedList<Node>();
            while (ft.NodeKind == NodeKind.FuncTerm)
            {
                var fun = (FuncTerm)ft;
                var it = fun.Args.GetEnumerator();
                it.MoveNext();
                lst.AddLast(it.Current);
                it.MoveNext();
                ft = it.Current;
            }
            Contract.Assert(ft.NodeKind == NodeKind.Id);
            return lst;
        }

        private void printPair(TextWriter writer, int indent, Node ft)
        {
            Contract.Assert(ft.NodeKind == NodeKind.FuncTerm);
            var fun = (FuncTerm)ft;
            var it = fun.Args.GetEnumerator();
            //the key
            it.MoveNext();
            var key = it.Current;
            print(writer, indent, key, false, true);
            //separator
            writer.Write(" : ");
            //the value
            it.MoveNext();
            var value = it.Current;
            print(writer, indent, value, false, false);
        }

        private void printObject(TextWriter writer, int indent, FuncTerm ft)
        {
            var pairs = collectElts(ft);
            var innerIndent = indent + tabStop;
            writer.WriteLine("{");
            foreach (var p in pairs){
                printPair(writer, innerIndent, p);
                writer.WriteLine(","); //TODO but the last
            }
            fillIndent(writer, indent);
            writer.Write("}");
        }

        private void printArray(TextWriter writer, int indent, FuncTerm ft)
        {
            var elts = collectElts(ft);
            var innerIndent = indent + tabStop;
            writer.WriteLine("[");
            foreach (var p in elts)
            {
                print(writer, innerIndent, p, false, true);
                writer.WriteLine(","); //TODO but the last
            }
            fillIndent(writer, indent);
            writer.Write("]");
        }

        private void print(TextWriter writer, int indent, Node node, bool newLine, bool fill)
        {
            if (fill) fillIndent(writer, indent);
            switch (node.NodeKind)
            {
                case NodeKind.Cnst:
                    var cst = (Cnst)node;
                    writer.Write(cst.GetStringValue());
                    if (newLine) writer.WriteLine();
                    return;

                case NodeKind.Id:
                    var id = ((Id)node).Name;
                    if (id == EmptyObj)
                    {
                        writer.Write("{}");
                    }
                    else if (id == EmptyArray)
                    {
                        writer.Write("[]");
                    }
                    else if (id == Null)
                    {
                        writer.Write("null");
                    }
                    else if (id == True)
                    {
                        writer.Write("true");
                    }
                    else if (id == False)
                    {
                        writer.Write("false");
                    }
                    else
                    {
                        writer.Write(id);
                    }
                    if (newLine) writer.WriteLine();
                    return;

                case NodeKind.FuncTerm:
                    var fun = (FuncTerm)node;
                    var funName = ((Id)fun.Function).Name;
                    if (funName == Members)
                    {
                        printObject(writer, indent, fun);
                    }
                    else if (funName == Elements)
                    {
                        printArray(writer, indent, fun);
                    }
                    else if (funName == Pair)
                    {
                        printArray(writer, indent, fun);
                    }
                    else
                    {
                        throw new FormatException("unknown function kind: " + funName);
                    }
                    if (newLine) writer.WriteLine();
                    return;
                    
                default:
                    throw new FormatException("unknown NodeKind: " + node.NodeKind);
            }
        }

        public bool Render(Configuration config, TextWriter writer, AST<Node> ast, out List<Flag> flags)
        {
            flags = new List<Flag>();
            try
            {
                print(writer, 0, ast.Node, true, true);
                return true;
            }
            catch (Exception e)
            {
                flags.Add(new Flag(
                    SeverityKind.Error,
                    ast.Node.Span,
                    Constants.QuotationError.ToString(e.Message),
                    Constants.QuotationError.Code));
                return false;
            }
        }

    }
}
