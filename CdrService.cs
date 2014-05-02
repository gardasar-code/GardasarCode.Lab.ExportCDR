using System;
using System.Collections.Concurrent;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using Oracle.DataAccess.Client;

namespace ExportCdr
{
    public class CdrService : ServiceBase
    {
        private static ConcurrentBag<int> _ids;
        private readonly ManualResetEvent _stop;
        private readonly Thread _thread;

        public CdrService()
        {
            ServiceName = "ExportCdr";
            CanPauseAndContinue = false;
            
            _thread = new Thread(() => _ = EntryPoint());
            _stop = new ManualResetEvent(false);
        }

        protected override void OnStart(string[] args)
        {
            _thread.Start();
        }

        protected override void OnStop()
        {
            _stop.Set();

            do
            {
                RequestAdditionalTime(31000);
            } while (!_thread.Join(30000)); // 30 sec
        }

        private async Task EntryPoint()
        {
            try
            {
                var timeoutAfterError = int.Parse(ConfigurationManager.AppSettings["timeoutAfterError"]);

                do
                {
                    await MainLoop();
                } while (!_stop.WaitOne(timeoutAfterError));
            }
            catch (Exception exception)
            {
                Logger.Write("EntryPoint:\n" + exception.Message);
                throw;
            }
        }

        private Task MainLoop()
        {
            OracleConnection conn = null;

            try
            {
                var orasource = ConfigurationManager.ConnectionStrings["orasource"].ConnectionString;
                var folder = ConfigurationManager.AppSettings["cdrpath"];
                var timeoutAfterWork = int.Parse(ConfigurationManager.AppSettings["timeoutAfterWork"]);
                var minSize = int.Parse(ConfigurationManager.AppSettings["minSize"]);
                var maxSize = int.Parse(ConfigurationManager.AppSettings["maxSize"]);

                var maxRec = int.Parse(ConfigurationManager.AppSettings["maxRec"]);

                conn = new OracleConnection(orasource);
                conn.Open();

                var pool = new PoolMan(conn, minSize, maxSize, -1, 0, null);
                var writer = new CdrWriter(conn, folder, maxRec);

                var ranges = Enumerable.Range(0, minSize).Reverse().ToArray();

                do
                {
                    Logger.WriteInfo("MERGE POOLS STARTS");
                    var sw = new Stopwatch();
                    sw.Start();

                    #region Merge

                    _ids = new ConcurrentBag<int>();

                    async void Action(int i)
                    {
                        await new PoolMan(conn, minSize, maxSize, i, ranges.Length, _ids).Merge(orasource);
                    }

                    var res = Parallel.ForEach(ranges, new ParallelOptions { MaxDegreeOfParallelism = Convert.ToInt32(Math.Ceiling(Environment.ProcessorCount * 63 * 1.0)) }, Action);

                    var ts = sw.Elapsed;

                    if (res.IsCompleted)
                    {
                        var count = _ids.Sum();
                        Logger.WriteInfo($"MERGE POOLS ADDED {count} RECORDS TOTAL, TIME ELAPSED {ts.Hours:00}:{ts.Minutes:00}:{ts.Seconds:00}");

                        if (count > 0)
                        {
                            #region Export

                            while (!_stop.WaitOne(0))
                                if (writer.Export() == 0)
                                {
                                    break;
                                }

                            #endregion
                        }
                    }

                    sw.Stop();
                    ts = sw.Elapsed;
                    Logger.WriteInfo($"MERGE POOLS STOP, TIME ELAPSED {ts.Hours:00}:{ts.Minutes:00}:{ts.Seconds:00}");

                    #endregion

                    pool.Clean();
                } while (!_stop.WaitOne(timeoutAfterWork));
            }
            catch (Exception exception)
            {
                Logger.Write($"MAINLOOP ERROR: {exception.GetType().Name} {exception.Message}");
            }
            finally
            {
                if (conn != null && conn.State != ConnectionState.Closed)
                {
                    conn.Close();
                }
            }

            return Task.CompletedTask;
        }
    }
}