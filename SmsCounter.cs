namespace ExportCdr
{
    internal class SmsCounter
    {
        private readonly string _onLimit, _offLimit;
        public readonly string Un, GroupId;
        public int Limit;

        public SmsCounter(string un, string groupId, int limit, string onLimit, string offLimit)
        {
            Un = un;
            GroupId = groupId;
            Limit = limit;
            _onLimit = onLimit;
            _offLimit = offLimit;
        }
        
        public bool GetService(out string code)
        {
            bool result;

            if (Limit > 0)
            {
                Limit--;
                code = _onLimit;
                result = true;
            }
            else
            {
                code = _offLimit;
                result = false;
            }

            return result;
        }
    }
}