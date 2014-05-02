using System;
using System.Collections.Specialized;
using Oracle.DataAccess.Client;

namespace ExportCdr
{
    internal class SmsRating
    {
        private readonly HybridDictionary _cache;
        private readonly OracleConnection _conn;

        public SmsRating(OracleConnection conn)
        {
            _conn = conn;
            _cache = new HybridDictionary();
        }

        public SmsCounter GetCounter(string un, string groupId)
        {
            if (_cache[un + groupId] is SmsCounter cnt)
            {
                return cnt;
            }

            try
            {
                var cmd = _conn.CreateCommand();

                cmd.CommandText =
                    "select limit, on_limit, off_limit from quotes " +
                    "where un = :un and group_id = :group_id for update of limit";

                cmd.Parameters.Add("un", un);
                cmd.Parameters.Add("group_id", groupId);

                var limit = 0;
                string onLimit = null, offLimit = null;

                using (var rdr = cmd.ExecuteReader())
                {
                    if (rdr.Read())
                    {
                        limit = (int)rdr.GetDecimal(0);
                        onLimit = rdr.GetString(1);
                        offLimit = rdr.GetString(2);
                    }
                }

                cnt = new SmsCounter(un, groupId, limit, onLimit, offLimit);

                // save to cache
                _cache[un + groupId] = cnt;
            }
            catch (Exception ex)
            {
                Logger.Write($"SmsRating.GetCounter(un={un}, group_id={groupId}): {ex.Message}");
                throw;
            }

            return cnt;
        }

        public void Commit()
        {
            if (_cache.Count == 0)
            {
                return;
            }

            try
            {
                var cmd = _conn.CreateCommand();
                cmd.CommandText =
                    "update quotes set limit = :limit where un = :un and group_id = :group_id";

                var oraLimit = cmd.Parameters.Add("limit", OracleDbType.Decimal);
                var oraUn = cmd.Parameters.Add("un", OracleDbType.Varchar2);
                var oraGroupId = cmd.Parameters.Add("group_id", OracleDbType.Varchar2);

                foreach (SmsCounter cnt in _cache.Values)
                {
                    oraLimit.Value = cnt.Limit;
                    oraUn.Value = cnt.Un;
                    oraGroupId.Value = cnt.GroupId;

                    cmd.ExecuteNonQuery();
                }

                _cache.Clear();
            }
            catch (Exception ex)
            {
                Logger.Write("SmsRating.Commit: " + ex.Message);
                throw;
            }
        }
    }
}