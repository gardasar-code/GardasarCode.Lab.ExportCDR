using System.IO;
using Oracle.DataAccess.Client;

namespace ExportCdr
{
    internal class Cdr
    {
        private readonly object[] _values;

        public Cdr(object[] values)
        {
            _values = values;
        }

        public decimal Id => (decimal)_values[0];

        public bool IsSms => (short)_values[6] == 7;

        public string Bn
        {
            get => (string)_values[4];

            set => _values[4] = value;
        }

        public string Un => _values[2] as string;

        public string Service
        {
            get => _values[13] as string;

            set => _values[13] = value;
        }

        public decimal Length
        {
            get => (decimal)_values[8];

            set => _values[8] = value;
        }

        public bool IsTpopo => Bn?.Contains(":::") == true;

        public string Po => Bn?.Substring(Bn.LastIndexOf(':') + 1);

        public void Write(StreamWriter sw)
        {
            sw.WriteLine(string.Join(";", _values));
        }
    }

    internal static class OracleDataReaderEx
    {
        public static bool ReadCdr(this OracleDataReader rdr, out Cdr c)
        {
            if (rdr.Read())
            {
                var values = new object[rdr.FieldCount];
                rdr.GetValues(values);
                c = new Cdr(values);
                return true;
            }

            c = null;
            return false;
        }
    }
}