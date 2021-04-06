﻿using System;
using System.Data;
using System.Globalization;
using System.Threading.Tasks;
using NpgsqlTypes;
using NUnit.Framework;

namespace Npgsql.Tests.Types
{
    public class NumericTests : MultiplexingTestBase
    {
        static readonly object[] ReadWriteCases = new[]
        {
            new object[] { "0.0000000000000000000000000001::numeric", 0.0000000000000000000000000001M },
            new object[] { "0.000000000000000000000001::numeric", 0.000000000000000000000001M },
            new object[] { "0.00000000000000000001::numeric", 0.00000000000000000001M },
            new object[] { "0.0000000000000001::numeric", 0.0000000000000001M },
            new object[] { "0.000000000001::numeric", 0.000000000001M },
            new object[] { "0.00000001::numeric", 0.00000001M },
            new object[] { "0.0001::numeric", 0.0001M },
            new object[] { "1::numeric", 1M },
            new object[] { "10000::numeric", 10000M },
            new object[] { "100000000::numeric", 100000000M },
            new object[] { "1000000000000::numeric", 1000000000000M },
            new object[] { "10000000000000000::numeric", 10000000000000000M },
            new object[] { "100000000000000000000::numeric", 100000000000000000000M },
            new object[] { "1000000000000000000000000::numeric", 1000000000000000000000000M },
            new object[] { "10000000000000000000000000000::numeric", 10000000000000000000000000000M },

            new object[] { "1E-28::numeric", 0.0000000000000000000000000001M },
            new object[] { "1E-24::numeric", 0.000000000000000000000001M },
            new object[] { "1E-20::numeric", 0.00000000000000000001M },
            new object[] { "1E-16::numeric", 0.0000000000000001M },
            new object[] { "1E-12::numeric", 0.000000000001M },
            new object[] { "1E-8::numeric", 0.00000001M },
            new object[] { "1E-4::numeric", 0.0001M },
            new object[] { "1E+0::numeric", 1M },
            new object[] { "1E+4::numeric", 10000M },
            new object[] { "1E+8::numeric", 100000000M },
            new object[] { "1E+12::numeric", 1000000000000M },
            new object[] { "1E+16::numeric", 10000000000000000M },
            new object[] { "1E+20::numeric", 100000000000000000000M },
            new object[] { "1E+24::numeric", 1000000000000000000000000M },
            new object[] { "1E+28::numeric", 10000000000000000000000000000M },

            new object[] { "11.222233334444555566667777888::numeric", 11.222233334444555566667777888M },
            new object[] { "111.22223333444455556666777788::numeric", 111.22223333444455556666777788M },
            new object[] { "1111.2222333344445555666677778::numeric", 1111.2222333344445555666677778M },

            new object[] { "+79228162514264337593543950335::numeric", +79228162514264337593543950335M },
            new object[] { "-79228162514264337593543950335::numeric", -79228162514264337593543950335M },

            // It is important to test rounding on both even and odd
            // numbers to make sure midpoint rounding is away from zero.
            new object[] { "1::numeric(10,2)", 1.00M },
            new object[] { "2::numeric(10,2)", 2.00M },

            new object[] { "1.2::numeric(10,1)", 1.2M },
            new object[] { "1.2::numeric(10,2)", 1.20M },
            new object[] { "1.2::numeric(10,3)", 1.200M },
            new object[] { "1.2::numeric(10,4)", 1.2000M },
            new object[] { "1.2::numeric(10,5)", 1.20000M },

            new object[] { "1.4::numeric(10,0)", 1M },
            new object[] { "1.5::numeric(10,0)", 2M },
            new object[] { "2.4::numeric(10,0)", 2M },
            new object[] { "2.5::numeric(10,0)", 3M },

            new object[] { "-1.4::numeric(10,0)", -1M },
            new object[] { "-1.5::numeric(10,0)", -2M },
            new object[] { "-2.4::numeric(10,0)", -2M },
            new object[] { "-2.5::numeric(10,0)", -3M },

            // Bug 2033
            new object[] { "0.0036882500000000000000000000", 0.0036882500000000000000000000M },
        };

        [Test]
        [TestCaseSource(nameof(ReadWriteCases))]
        public async Task Read(string query, decimal expected)
        {
            using var conn = await OpenConnectionAsync();
            using var cmd = new NpgsqlCommand("SELECT " + query, conn);
            Assert.That(
                decimal.GetBits((decimal)(await cmd.ExecuteScalarAsync())!),
                Is.EqualTo(decimal.GetBits(expected)));
            using var reader = await cmd.ExecuteReaderAsync();
            await reader.ReadAsync();
            var npgsqlDecimal = reader.GetFieldValue<NpgsqlDecimal>(0);
            Assert.That(npgsqlDecimal, Is.EqualTo((NpgsqlDecimal)expected));
            Assert.That((decimal)npgsqlDecimal, Is.EqualTo(expected));
            Assert.That(npgsqlDecimal.ToString(), Is.EqualTo(expected.ToString(CultureInfo.InvariantCulture)));
        }

        [Test]
        [TestCaseSource(nameof(ReadWriteCases))]
        public async Task Write(string query, decimal expected)
        {
            using var conn = await OpenConnectionAsync();
            using var cmd = new NpgsqlCommand("SELECT @p, @p2, @p = " + query + " AND @p2 = " + query, conn);
            cmd.Parameters.AddWithValue("p", expected);
            cmd.Parameters.AddWithValue("p2", (NpgsqlDecimal)expected);
            using var rdr = await cmd.ExecuteReaderAsync();
            rdr.Read();
            Assert.That(decimal.GetBits(rdr.GetFieldValue<decimal>(0)), Is.EqualTo(decimal.GetBits(expected)));
            Assert.That(rdr.GetFieldValue<NpgsqlDecimal>(1), Is.EqualTo((NpgsqlDecimal)expected));
            Assert.That(rdr.GetFieldValue<bool>(2));
        }

        [Test]
        public async Task Mapping()
        {
            using var conn = await OpenConnectionAsync();
            using var cmd = new NpgsqlCommand("SELECT @p1, @p2, @p3, @p4", conn);
            cmd.Parameters.Add(new NpgsqlParameter("p1", NpgsqlDbType.Numeric) { Value = 8M });
            cmd.Parameters.Add(new NpgsqlParameter("p2", DbType.Decimal) { Value = 8M });
            cmd.Parameters.Add(new NpgsqlParameter("p3", DbType.VarNumeric) { Value = 8M });
            cmd.Parameters.Add(new NpgsqlParameter("p4", 8M));

            using var rdr = await cmd.ExecuteReaderAsync();
            rdr.Read();
            for (var i = 0; i < cmd.Parameters.Count; i++)
            {
                Assert.That(rdr.GetFieldType(i), Is.EqualTo(typeof(decimal)));
                Assert.That(rdr.GetDataTypeName(i), Is.EqualTo("numeric"));
                Assert.That(rdr.GetValue(i), Is.EqualTo(8M));
                Assert.That(rdr.GetProviderSpecificValue(i), Is.EqualTo(8M));
                Assert.That(rdr.GetFieldValue<decimal>(i), Is.EqualTo(8M));
                Assert.That(rdr.GetFieldValue<byte>(i), Is.EqualTo(8));
                Assert.That(rdr.GetFieldValue<short>(i), Is.EqualTo(8));
                Assert.That(rdr.GetFieldValue<int>(i), Is.EqualTo(8));
                Assert.That(rdr.GetFieldValue<long>(i), Is.EqualTo(8));
                Assert.That(rdr.GetFieldValue<float>(i), Is.EqualTo(8.0f));
                Assert.That(rdr.GetFieldValue<double>(i), Is.EqualTo(8.0d));
            }
        }

        [Test, Description("Tests that when Numeric value does not fit in a System.Decimal and reader is in ReaderState.InResult, the value was read wholly and it is safe to continue reading")]
        [Timeout(5000)]
        public async Task ReadOverflowIsSafe()
        {
            using var conn = await OpenConnectionAsync();
            //This 29-digit number causes OverflowException. Here it is important to have unread column after failing one to leave it ReaderState.InResult
            using var cmd = new NpgsqlCommand(@"SELECT (0.20285714285714285714285714285)::numeric, generate_series FROM generate_series(1, 2)", conn);
            using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess);
            var i = 1;

            while (reader.Read())
            {
                Assert.That(() => reader.GetDecimal(0),
                    Throws.Exception
                        .With.TypeOf<OverflowException>()
                        .With.Message.EqualTo("Numeric value does not fit in a System.Decimal"));
                var intValue = reader.GetInt32(1);

                Assert.That(intValue, Is.EqualTo(i++));
                Assert.That(conn.FullState, Is.EqualTo(ConnectionState.Open | ConnectionState.Fetching));
                Assert.That(conn.State, Is.EqualTo(ConnectionState.Open));
                Assert.That(reader.State, Is.EqualTo(ReaderState.InResult));
            }
        }

        [Test]
        public async Task ReadLarge()
        {
            var rnd = new System.Random(123);
            var str = new System.Text.StringBuilder();
            for (var i = 0; i < 131072; i++) str.Append(rnd.Next(0, 10));
            str.Append('.');
            for (var i = 0; i < 16383; i++) str.Append(rnd.Next(0, 10));
            var large = str.ToString();
            var parsed = NpgsqlDecimal.Parse(large);

            using var conn = await OpenConnectionAsync();
            using var cmd = new NpgsqlCommand("SELECT '" + large + "'::numeric, @p, '1e+40'::numeric, @nan", conn);
            var p = new NpgsqlParameter("p", parsed);
            cmd.Parameters.Add(p);
            Assert.That(p.NpgsqlDbType, Is.EqualTo(NpgsqlDbType.Numeric));
            cmd.Parameters.Add(new NpgsqlParameter("nan", NpgsqlDecimal.NaN));
            using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess);
            await reader.ReadAsync();
            var d1 = (NpgsqlDecimal)reader.GetProviderSpecificValue(0);
            var d2 = (NpgsqlDecimal)reader.GetProviderSpecificValue(1);
            var d3 = reader.GetFieldValue<NpgsqlDecimal>(2);
            var d4 = reader.GetFieldValue<NpgsqlDecimal>(3);
            Assert.That(d1, Is.EqualTo(d2));
            Assert.That(d1, Is.EqualTo(parsed));
            Assert.That(d1.ToString(), Is.EqualTo(large));
            Assert.That(d3, Is.EqualTo(NpgsqlDecimal.Parse("1e+40")));
            Assert.That(d4, Is.EqualTo(NpgsqlDecimal.NaN));
        }

        public NumericTests(MultiplexingMode multiplexingMode) : base(multiplexingMode) {}
    }
}
