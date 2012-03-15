﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace DataAccess
{
    public class AssertException : Exception
    {
        public AssertException(string message)
            : base(message)
        { }
    }

    public static class Utility
    {
        // Helper for Dictionaries. 
        public static TValue GetOrCreate<TKey, TValue>(this Dictionary<TKey, TValue> dict, TKey lookup) where TValue : new()
        {
            TValue r;
            if (dict.TryGetValue(lookup, out r))
            {
                return r;
            }
            r = new TValue();
            dict[lookup] = r;
            return r;
        }

        // Helper for Dictionaries. Useful when TValue doesn't have a default ctor (such as with immutable objects like Tuples)
        public static TValue GetOrDefault<TKey, TValue>(this Dictionary<TKey, TValue> dict, TKey lookup, TValue defaultValue)
        {
            TValue r;
            if (dict.TryGetValue(lookup, out r))
            {
                return r;
            }
            r = defaultValue;
            dict[lookup] = r;
            return r;
        }

        public static void Assert(bool f)
        {
            Assert(f, String.Empty);
        }
        public static void Assert(bool f, string message)
        {
            if (!f)
            {
                throw new AssertException(message);
                //Debugger.Break();
            }
        }

        // Case insensitive string compare
        internal static bool Compare(string a, string b)
        {
            return string.Compare(a, b, true) == 0;
        }

        // All strings become upper case (for comparison)
        public static Dictionary<TKey, TValue> ToDict<TKey, TValue>(DataTable table, string keyName, string valueName)
        {
            // $$$ Should this be on DataTable?
            int cKey = Utility.GetColumnIndexFromName(table.ColumnNames, keyName);
            int cValue = Utility.GetColumnIndexFromName(table.ColumnNames, valueName);
            return ToDict<TKey, TValue>(table, cKey, cValue);
        }

        public static Dictionary<TKey, TValue> ToDict<TKey, TValue>(DataTable table)
        {
            // Assume first two
            return ToDict<TKey, TValue>(table, 0, 1);
        }

        // column ids to use for keys and values.
        public static Dictionary<TKey, TValue> ToDict<TKey, TValue>(DataTable table, int cKey, int cVal)
        {
            Dictionary<TKey, TValue> d = new Dictionary<TKey, TValue>();
            for (int row = 0; row < table.NumRows; row++)
            {
                TKey k = Convert<TKey>(table.Columns[cKey].Values[row]);
                TValue v = Convert<TValue>(table.Columns[cVal].Values[row]);
                d[k] = v;
            }
            return d;
        }

        static T Convert<T>(string s)
        {
            return (T)System.Convert.ChangeType(s.ToUpperInvariant(), typeof(T));
        }


        // Dynamically Flatten. 
        // $$$ Need way to gaurantee that flatten order matches column names.
        public static DataTable ToTableX<T>(IEnumerable<T> a, params string[] columnNames)
        {
            // $$$ How to infer column names?
            // Flatten doesn't have a definitive order.
            // If we had more smart collections, we could infer. 
            
            int count = a.Count();

            DataTable d = new DataTable();

            // Alloc columns
            Column[] cs = new Column[columnNames.Length];
            for (int i = 0; i < columnNames.Length; i++)
            {
                cs[i] = new Column(columnNames[i], count);
            }

            // Fill in rows
            int row = 0;
            foreach (T item in a)
            {
                string[] values = Flatten(item);
                Utility.Assert(values.Length == columnNames.Length);

                for (int i = 0; i < columnNames.Length; i++)
                {
                    cs[i].Values[row] = values[i];
                }

                row++;
            }

            d.Columns = cs;
            return d;            
        }

        static string[] Flatten<T>(T item)
        {
            List<string> vals = new List<string>();            
            FlattenWorker(item, vals);         
            return vals.ToArray();
        }

        static void FlattenWorker(object item, List<string> vals)
        {
            Type t = item.GetType();

            // May need to flatten recursively
            if (t.IsPrimitive)
            {
                vals.Add(item.ToString());
                return;
            }

            if ((t == typeof(string)) || (t == typeof(DateTime)) || t.IsEnum || (t == typeof(DiscreteValue)))
            {
                vals.Add(item.ToString());
                return;
            }

            if (t.IsGenericType)
            {
                Type t2 = t.GetGenericTypeDefinition();

                if (t2 == typeof(KeyValuePair<,>))
                {
                    object key = GetMember(item, "Key");
                    FlattenWorker(key, vals);

                    object value = GetMember(item, "Value");
                    FlattenWorker(value, vals);
                }
                return;
            }

            // It's a class, add public properties of the class.
            // $$$ If the class is polymorphic, then this could change for different instances.
            {
                PropertyInfo[] ps = t.GetProperties();
                foreach (var p in ps)
                {
                    FlattenWorker(GetMember(item, p.Name), vals);
                }
                return;
            }

            // If it's a tuple, 


            // If is a key-value pair?
            throw new NotImplementedException();
        }

        static object GetMember(object o, string memberName)
        {
            Type t = o.GetType();
            PropertyInfo p = t.GetProperty(memberName);
            return p.GetValue(o, null);
        }

        public static DataTable ToTable<T>(IEnumerable<T> a)
        {
            string[] columnNames = Array.ConvertAll(typeof(T).GetProperties(), prop => prop.Name);

            return ToTableX<T>(a, columnNames);
        }

        // $$$ Merge with the more dynamic ToTable.
        public static DataTable ToTable<T1, T2>(Tuple<T1, T2>[] a, string name1, string name2)
        {
            DataTable d = new DataTable();

            int count = a.Length;
            Column cKeys = new Column(name1, count);
            Column cVals = new Column(name2, count);

            d.Columns = new Column[] { cKeys, cVals };

            int i = 0;
            foreach (var kv in a)
            {
                cKeys.Values[i] = kv.Item1.ToString();
                cVals.Values[i] = kv.Item2.ToString();
                i++;
            }
            return d;
        }
                
        // Helper to convert a dictionary into a data-table
        // Can be useful for saving as a CSV back into the filestore.
        public static DataTable ToTable<TKey, TValue>(IDictionary<TKey, TValue> dict, string keyName, string valName)
        {
            DataTable d = new DataTable();

            int count = dict.Count;
            Column cKeys = new Column(keyName, count);
            Column cVals = new Column(valName, count);

            d.Columns = new Column[] { cKeys, cVals };

            int i = 0;
            foreach (var kv in dict)
            {
                cKeys.Values[i] = kv.Key.ToString();
                cVals.Values[i] = kv.Value.ToString();
                i++;
            }
            return d;
        }

        // Convert a 2d dict into a 2d data table.
        // TKey1 is rows, TKey1 is columns.
        // Data table column names are obtained from key values.
        // Column 0 is set of row values.
        public static DataTable ToTable<TKey1, TKey2, TValue>(Dictionary2d<TKey1, TKey2, TValue> dict)
        {
            // TKey1 is rows, TKey2 is values.
            DataTable d = new DataTable();

            var rows = dict.Key1;
            int count = rows.Count();

            // Set columns
            var columns = dict.Key2.ToArray();
            {
                Column[] cs = new Column[columns.Length + 1];
                cs[0] = new Column("row name", count);
                for (int ic = 0; ic < columns.Length; ic++)
                {
                    cs[ic + 1] = new Column(columns[ic].ToString(), count);
                }
                d.Columns = cs;
            }

            // Add rows
            int i = 0;
            foreach (var row in rows)
            {
                d.Columns[0].Values[i] = row.ToString();
                for (int ic = 0; ic < columns.Length; ic++)
                {
                    d.Columns[ic + 1].Values[i] = dict[row, columns[ic]].ToString();
                }
                i++;
            }

            return d;
        }

#if false

        // $$$ Can some of these be moved to DataTable?
        // Separated out primarily to facilitate operating on big data. 
        // DataTable loads everything in memory.
        // These operate on (IFileStore,FileDescr) or strings. 

        public static string[] GetColumnNames(IFileStore fs, FileDescriptor fdInput)
        {
            using (TextReader sr = fs.OpenText(fdInput))
            {
                // First get columns.
                string header = sr.ReadLine();
                string[] columnNames = Reader.split(header, ',');
                return columnNames;
            }
        }

        public static int[] GetColumnIndexFromNames(IFileStore fs, FileDescriptor fdInput, string[] columnName)
        {
            int count = columnName.Length;
            int[] x = new int[count];
            for (int i = 0; i < count; i++)
            {
                x[i] = GetColumnIndexFromName(fs, fdInput, columnName[i]);
            }
            return x;
        }

        // Return 0-based index of column with matching name.
        // throws an exception if not found
        public static int GetColumnIndexFromName(IFileStore fs, FileDescriptor fdInput, string columnName)
        {
            string[] columnNames = GetColumnNames(fs, fdInput);
            return GetColumnIndexFromName(columnNames, columnName);
        }
#endif
        public static int GetColumnIndexFromName(IEnumerable<string> columnNames, string columnName)
        {
            int i = 0;
            foreach (string x in columnNames)
            {
                if (string.Compare(columnName, x, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    return i;
                }
                i++;
            }
            throw new InvalidOperationException("No column named '" + columnName + "'");
        }

    }
}