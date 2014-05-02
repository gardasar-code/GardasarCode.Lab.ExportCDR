using System;
using System.Collections.Concurrent;
using System.Data;
using System.Diagnostics;
using System.Threading.Tasks;
using Oracle.DataAccess.Client;
using Oracle.DataAccess.Types;

namespace ExportCdr
{
    internal class PoolMan
    {
        private readonly OracleConnection _conn;

        private readonly ConcurrentBag<int> _ids;
        private readonly int _maxSize;
        private readonly int _minSize;
        private readonly int _range;
        private readonly int _rangesLength;

        public PoolMan(OracleConnection conn, int minSize, int maxSize, int range, int rangesLength, ConcurrentBag<int> ids)
        {
            _conn = conn;
            _minSize = minSize;
            _maxSize = maxSize;
            _range = range;
            _ids = ids;
            _rangesLength = rangesLength;
        }

        public async Task<decimal> Merge(string orasource)
        {
            int count = 0;
            OracleConnection conn = null;
            OracleTransaction tnx = null;

            try
            {
                var d = _minSize / _rangesLength;
                var start = DateTime.Now.AddHours(-1 * (_range * d + d));
                var end = DateTime.Now.AddHours(-1 * _range * d);

                Logger.WriteInfo($"MERGE POOL {_range:D2}, DT-RANGE {start:dd.MM.yyyy HH:mm:ss} - {end:dd.MM.yyyy HH:mm:ss}");
                var sw = new Stopwatch();
                sw.Start();

                conn = new OracleConnection(orasource);
                conn.Open();

                tnx = conn.BeginTransaction();

                var cmd = conn.CreateCommand();
                cmd.CommandText =
                    "begin " +
                    "merge into pool " +
                    "using ( " +
                    "select cdr.id, cdr.sid, nvl(dt.un, cdr.un) as un, cdr.an, nvl2(dt.un, decode(cdr.dir, 0, decode(dt.un, dt.phone, dt.un, dt.un || ':::' || cdr.un), decode(dt.un, dt.phone, cdr.bn, cdr.bn || ':::' || cdr.un) ), cdr.bn) as bn, cdr.dir, cdr.type, cdr.starttime, cdr.duration - cdr.waitdur as duration, cdr.ivrdur, cdr.recdur, cdr.subtype, cdr.handlerid, decode(cdr.type, 6, cdr.tag2, 7, cdr.tag2) as tag, substr(dt.contract, 2, 2) as region, REGEXP_SUBSTR(cdr.log, 'SRC\\|([^,]+),(\\d+)', 1, 1, NULL, 1) AS src_ip, REGEXP_SUBSTR(cdr.log, 'SRC\\|([^,]+),(\\d+)', 1, 1, NULL, 2) AS src_port, REGEXP_SUBSTR(cdr.log, 'DST\\|([^,]+),(\\d+)', 1, 1, NULL, 1) AS dst_ip, REGEXP_SUBSTR(cdr.log, 'DST\\|([^,]+),(\\d+)', 1, 1, NULL, 2) AS dst_port from cdr join resourcehandlers rh on cdr.handlerid = rh.id and rh.exportcdr = 1 left outer join un_data dt on cdr.un = dt.phone " +
                    "and cdr.type != 6 " +
                    "where cdr.starttime > :pool_start and cdr.starttime <= :pool_end " +
                    "and (cdr.handlerid = 8 or (cdr.handlerid != 8 and cdr.result = 1280)) " +
                    ") cdr " +
                    "on (cdr.id = pool.id) " +
                    "when not matched then " +
                    "insert(id, sid, un, an, bn, dir, type, starttime, duration, ivrdur, recdur, subtype, handlerid, tag, region, src_ip, src_port, dst_ip, dst_port) values(cdr.id, cdr.sid, cdr.un, cdr.an, cdr.bn, cdr.dir, cdr.type, cdr.starttime, cdr.duration, cdr.ivrdur, cdr.recdur, cdr.subtype, cdr.handlerid, cdr.tag, cdr.region, cdr.src_ip, cdr.src_port, cdr.dst_ip, cdr.dst_port); " +
                    ":ret := sql%rowcount; " +
                    "end;";
                
                cmd.BindByName = true;
                cmd.Parameters.Add("pool_start", OracleDbType.Date).Value = start;
                cmd.Parameters.Add("pool_end", OracleDbType.Date).Value = end;
                //cmd.Parameters.Add("pool_size", _minSize);

                var outParam = cmd.Parameters.Add("ret", OracleDbType.Decimal, ParameterDirection.Output);

                // always return -1. Workaround is plsql block and SQL%ROWCOUNT
                await cmd.ExecuteNonQueryAsync();
                
                count = Convert.ToInt32((decimal)(OracleDecimal)outParam.Value);
                _ids.Add(count);
                
                sw.Stop();
                var ts = sw.Elapsed;
                Logger.WriteInfo($"MERGE POOL {_range:D2} ADDED {count} RECORDS, DT-RANGE {start:dd.MM.yyyy HH:mm:ss} - {end:dd.MM.yyyy HH:mm:ss}, TIME ELAPSED {ts.Hours:00}:{ts.Minutes:00}:{ts.Seconds:00}");
                
                tnx.Commit();
            }
            catch (Exception ex)
            {
                Logger.Write($"MERGE POOL {_range:D2} ERROR: {ex.GetType().Name} {ex.Message}");
                tnx?.Rollback();
            }
            finally
            {
                conn?.Close();
            }

            return count;
        }

        public void Clean()
        {
            OracleTransaction tnx = null;

            try
            {
                Logger.WriteInfo("CLEAN POOL START");
                var sw = new Stopwatch();
                sw.Start();

                tnx = _conn.BeginTransaction();
                var cmd = _conn.CreateCommand();
                cmd.CommandText = "delete from pool where starttime < (sysdate - numtodsinterval(:pool_size, 'hour')) and exp = 1";
                cmd.Parameters.Add("pool_size", _maxSize);
                cmd.ExecuteNonQuery();
                tnx.Commit();

                sw.Stop();
                var ts = sw.Elapsed;
                Logger.WriteInfo($"CLEAN POOL STOP, TIME ELAPSED {ts.Hours:00}:{ts.Minutes:00}:{ts.Seconds:00}");
            }
            catch (Exception ex)
            {
                tnx?.Rollback();
                Logger.Write($"CLEAN POOL ERROR: {ex.GetType().Name} {ex.Message}");
            }
        }
    }
}