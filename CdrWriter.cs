using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Text;
using Oracle.DataAccess.Client;

namespace ExportCdr
{
    internal class CdrWriter
    {
        private readonly OracleConnection _conn;
        private readonly string _folder;
        private readonly int _maxRec;

        public CdrWriter(OracleConnection conn, string folder, int maxRec)
        {
            _conn = conn;
            _folder = folder;
            _maxRec = maxRec;
        }

        private static int ProcessCdr(StreamWriter sw, SmsRating rating, Cdr cdr)
        {
            var lines = 0;

            try
            {
                if (cdr.IsSms)
                {
                    var counter = rating.GetCounter(cdr.IsTpopo ? cdr.Po : cdr.Un, cdr.Service);
                    var total = rating.GetCounter(cdr.IsTpopo ? cdr.Po : cdr.Un, "TOTAL");

                    var len = (int)cdr.Length;
                    cdr.Length = 0; // clear cdr.duration

                    string s;
                    var groupId = cdr.Service;

                    for (var i = 0; i < len; i++)
                    {
                        if (total.GetService(out s))
                        {
                            counter.GetService(out s);
                        }
                        else
                        {
                            string s1;
                            counter.GetService(out s1);

                            if (groupId == "MTS")
                            {
                                s = s1;
                            }
                        }

                        cdr.Service = s;
                        cdr.Write(sw);
                        lines++;
                    }
                }
                else
                {
                    var s = cdr.Bn;

                    if (!string.IsNullOrEmpty(s))
                    {
                        if (s.StartsWith("8"))
                        {
                            var ind = s.IndexOf(":::");

                            if ((ind > 0 ? ind : s.Length) >= 11)
                            {
                                cdr.Bn = s.Substring(1);
                            }
                        }
                    }

                    cdr.Write(sw);
                    lines++;
                }
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message + " cdr.id = " + cdr.Id);
            }

            return lines;
        }

        public int Export()
        {
            var count = 0;
            OracleTransaction tnx = null;

            try
            {
                Logger.WriteInfo("EXPORT FILE START");
                var stopWatch = new Stopwatch();
                stopWatch.Start();

                tnx = _conn.BeginTransaction();
                var rating = new SmsRating(_conn);

                var cmd = _conn.CreateCommand();

                cmd.CommandText =
                    "select id, sid, un, an, bn, dir, type, to_char(starttime, 'yyyymmddhh24miss') as starttime, duration, ivrdur, recdur, subtype, handlerid, tag, region, src_ip, src_port, dst_ip, dst_port from pool " +
                    "where exp = 0 and rownum <= :max_rec order by id asc for update skip locked";

                cmd.Parameters.Add("max_rec", _maxRec);

                // temporary file name
                var tmpPath = _folder + @"\~cdr.tmp";

                decimal minId = 0;
                var list = new List<decimal>();

                var stopWatchFile = new Stopwatch();
                stopWatchFile.Start();

                using (var rdr = cmd.ExecuteReader())
                {
                    // fetch cdr
                    Cdr cdr;
                    if (rdr.ReadCdr(out cdr))
                    {
                        // create temp file only if rdr.Read() succeeded
                        using (var sw = new StreamWriter(tmpPath, false, Encoding.GetEncoding(1251)))
                        {
                            ProcessCdr(sw, rating, cdr);
                            list.Add(cdr.Id);

                            minId = cdr.Id;

                            count++;

                            while (rdr.ReadCdr(out cdr))
                            {
                                ProcessCdr(sw, rating, cdr);
                                list.Add(cdr.Id);

                                if (cdr.Id < minId)
                                {
                                    minId = cdr.Id;
                                }

                                count++;
                            }

                            sw.Close();
                        }
                    }
                }

                stopWatchFile.Stop();
                var tsFile = stopWatchFile.Elapsed;
                Logger.WriteInfo($"EXPORT FILE FETCH {count,6:D6} CDRs, TIME ELAPSED {tsFile.Hours:00}:{tsFile.Minutes:00}:{tsFile.Seconds:00}");

                if (count > 0)
                {
                    var stopWatchtsrating = new Stopwatch();
                    stopWatchtsrating.Start();

                    // save counters in DB
                    rating.Commit();

                    stopWatchtsrating.Stop();
                    var tsrating = stopWatchtsrating.Elapsed;
                    Logger.WriteInfo($"EXPORT FILE SAVE COUNTERS, TIME ELAPSED {tsrating.Hours:00}:{tsrating.Minutes:00}:{tsrating.Seconds:00}");

                    var stopWatchMrark = new Stopwatch();
                    stopWatchMrark.Start();

                    // mark records as exported DB
                    cmd.Parameters.Clear();

                    cmd.CommandText = "update pool set exp = 1 where id = :id";

                    cmd.ArrayBindCount = list.Count;
                    cmd.Parameters.Add("id", OracleDbType.Decimal, list.ToArray(), ParameterDirection.Input);

                    cmd.ExecuteNonQuery();

                    list.Clear();

                    stopWatchMrark.Stop();
                    var tsMark = stopWatchMrark.Elapsed;
                    Logger.WriteInfo($"EXPORT FILE MARK CDRs RECORDS, TIME ELAPSED {tsMark.Hours:00}:{tsMark.Minutes:00}:{tsMark.Seconds:00}");

                    var stopWatchswitchFile = new Stopwatch();
                    stopWatchswitchFile.Start();
                    // switch files
                    var fileName = $"cdr_{minId}_{count}.txt";
                    var filePath = $"{_folder}\\{fileName}";
                    var fileCopyPath = $"{_folder}\\copy\\{fileName}";

                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                    }

                    if (File.Exists(fileCopyPath))
                    {
                        File.Delete(fileCopyPath);
                    }

                    File.Copy(tmpPath, fileCopyPath);
                    File.Move(tmpPath, filePath);

                    stopWatchswitchFile.Stop();
                    var tsswitchFile = stopWatchswitchFile.Elapsed;
                    Logger.WriteInfo($"EXPORT FILE RENAME/MOVE '{fileCopyPath}' TO '{fileName}', TIME ELAPSED {tsswitchFile.Hours:00}:{tsswitchFile.Minutes:00}:{tsswitchFile.Seconds:00}");

                    stopWatch.Stop();
                    var ts = stopWatch.Elapsed;
                    Logger.WriteInfo($"EXPORT FILE END '{fileName}' TIME ELAPSED: {ts.Hours:00}:{ts.Minutes:00}:{ts.Seconds:00}");
                }

                tnx.Commit();
            }
            catch (Exception ex)
            {
                count = 0;
                tnx?.Rollback();
                Logger.Write($"EXPORT FILE ERROR: {ex.Message}");
                throw;
            }

            return count;
        }
    }
}