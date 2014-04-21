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

    class PositionalStreamReader: IDisposable
    {
        private StreamReader reader;
        private int line = 0;
        private int col = 0;
        private SourcePositioner positioner;

        public PositionalStreamReader(Stream input, SourcePositioner pos)
        {
            reader = new StreamReader(input);
            positioner = pos;
        }

        public void Dispose()
        {
            reader.Dispose();
        }

        public int Read()
        {
            var cr = Convert.ToInt32('\r');
            var lf = Convert.ToInt32('\n');
            var res = reader.Read();
            //CR + LF -> skip the CR
            if (res == cr && reader.Peek() == lf)
            {
                res = reader.Read();
            }
            if (res == cr || res == lf)
            {
                line += 1;
                col = 0;
            }
            return res;
        }

        public int Peek()
        {
            return reader.Peek();
        }

        //TODO this assumes no line return
        public int Read(char[] buffer, int length)
        {
            var count = reader.Read(buffer, 0, length);
            col += count;
            return count;
        }

        public Tuple<int, int> getPos()
        {
            return new Tuple<int, int>(line, col);
        }

        public int getLine()
        {
            return line;
        }

        public int getCol()
        {
            return col;
        }

        public SourcePositioner pos()
        {
            return positioner;
        }
    }
}