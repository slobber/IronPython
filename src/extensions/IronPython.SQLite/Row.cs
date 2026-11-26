// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.
//
// Copyright (c) Jeff Hardy 2010-2012.
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IronPython.Runtime;
using IronPython.Runtime.Operations;
using System.Collections;
using IronPython.Runtime.Exceptions;

namespace IronPython.SQLite
{
    public static partial class PythonSQLite
    {
        [PythonType]
        public class Row : IEnumerable
        {
            private PythonTuple data;
            private PythonTuple description;

            public Row(Cursor cursor, PythonTuple data)
            {
                this.data = data;
                this.description = cursor.description;
            }

            public override bool Equals(object obj)
            {
                Row other = obj as Row;

                if(other == null)
                    return false;

                if(object.ReferenceEquals(this, other))
                    return true;

                return this.description.Equals(other.description) && this.data.Equals(other.data);
            }

            public override int GetHashCode()
            {
                try
                {
                    return PythonOps.Hash(DefaultContext.Default, data);
                }
                catch (Exception)
                {
                    throw PythonOps.TypeError("unhashable type");
                }
            }

            public object __iter__()
            {
                return data;
            }

            public int __len__()
            {
                return data.Count;
            }

            public object this[object index]
            {
                get
                {
                    if (index is string text)
                    {
                        for(int i = 0; i < data.Count; ++i)
                        {
                            PythonTuple col_desc = (PythonTuple)description[i];
                            if (AsciiEqualsIgnoreCase(
                                    text,
                                    (string)col_desc[0]))
                            {
                                return data[i];
                            }
                        }

                        throw CreateThrowable(
                            PythonExceptions.IndexError,
                            text);
                    }

                    if (index is int integer)
                    {
                        int normalized = integer;
                        if (normalized < 0)
                        {
                            normalized += data.Count;
                        }
                        if (normalized < 0 || normalized >= data.Count)
                        {
                            throw PythonOps.IndexError(
                                "tuple index out of range");
                        }
                        return data[normalized];
                    }

                    if (index is System.Numerics.BigInteger big)
                    {
                        throw PythonOps.IndexError(
                            "cannot fit 'int' into an index-sized integer");
                    }

                    if (index is Slice slice)
                    {
                        return data[slice];
                    }

                    throw PythonOps.IndexError(
                        "row indices must be integers or strings");
                }
            }

            private static bool AsciiEqualsIgnoreCase(string left, string right)
            {
                if (left.Length != right.Length)
                {
                    return false;
                }
                for (int i = 0; i < left.Length; ++i)
                {
                    char a = left[i];
                    char b = right[i];
                    if (a >= 'A' && a <= 'Z')
                    {
                        a = (char)(a + ('a' - 'A'));
                    }
                    if (b >= 'A' && b <= 'Z')
                    {
                        b = (char)(b + ('a' - 'A'));
                    }
                    if (a != b)
                    {
                        return false;
                    }
                }
                return true;
            }

            public object GetItem(string s)
            {
                return this[s];
            }

            public object __getitem__(object index)
            {
                return this[index];
            }

            public object __getitem__(string key)
            {
                return this[key];
            }

            public PythonList keys()
            {
                PythonList list = new PythonList();

                for(int i = 0; i < data.Count; ++i)
                {
                    list.append(((PythonTuple)description[i])[0]);
                }

                return list;
            }

            #region IEnumerable Members

            public IEnumerator GetEnumerator()
            {
                return data.GetEnumerator();
            }

            #endregion
        }
    }
}
