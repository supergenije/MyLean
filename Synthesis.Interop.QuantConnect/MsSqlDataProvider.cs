using QuantConnect.Configuration;
using QuantConnect.Interfaces;
using Synthesis.Interop.Db.Model;

namespace Synthesis.Interop.QuantConnect
{
    public class MsSqlDataProvider:IDataProvider
    {
        private readonly string _connectionString = Config.Get("sql-connect-string", "");
        private readonly SynthesisContext _context = new SynthesisContext()

        public Stream Fetch(string key)
        {
            throw new NotImplementedException();
        }
    }
}
