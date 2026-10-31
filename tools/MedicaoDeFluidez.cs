using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace MhiagosControl
{
    public static partial class Tests
    {

        private static double Mediana(List<double> v)
        {
            List<double> c = new List<double>(v);
            c.Sort();
            return c.Count == 0 ? 0 : c[c.Count / 2];
        }
    }
}
