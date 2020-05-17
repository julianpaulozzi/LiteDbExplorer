using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Caliburn.Micro;
using LiteDB;
using LiteDbExplorer.Core;
using LiteDbExplorer.Wpf.Modules.Exception;

namespace LiteDbExplorer.Modules.DbQuery
{
    [ExportQueryViewHandler(Name, "Shell Command", 0)]
    public class ShellCommandQueryViewHandler : IQueryViewHandler
    {
        public const string Name = @"ShellCommand";

        public Task<IList<IScreen>> RunQuery(DatabaseReference databaseReference, IQueryHistoryProvider queryHistoryProvider, string query)
        {
            var result = new List<IScreen>();

            if (string.IsNullOrWhiteSpace(query))
            {
                return Task.FromResult<IList<IScreen>>(result);
            }

            var rawQueries = RemoveQueryComments(query)
                .Split(new[] { "db.", "DB." }, StringSplitOptions.RemoveEmptyEntries)
                .Select(q => $"db.{q.Trim()}")
                .ToList();

            var resultCount = 0;
            foreach (var rawQuery in rawQueries)
            {
                resultCount++;
                
                try
                {
                    var resultViewModel = IoC.Get<QueryResultViewModel>();
                    
                    IList<BsonValue> results;
                    using (resultViewModel.StartTime())
                    {
                        //results = databaseReference.RunCommand(rawQuery);
                    }
                    //resultViewModel.SetResult(
                    //    $"Result {resultCount}", 
                    //    rawQuery,
                    //    new QueryResult(results, UserDefinedCultureFormat.Default));

                    //result.Add(resultViewModel);
                }
                catch (Exception e)
                {
                    var title = $"Query {resultCount} Error";
                    var exceptionScreen = new ExceptionScreenViewModel(title, $"Error on Query {resultCount}:\n'{rawQuery}'", e);
                    result.Add(exceptionScreen);
                }
            }

            var queryHistory = new RawQueryHistory
            {
                QueryHandlerName = Name,
                RawQuery = query.Trim(),
                DatabaseLocation = databaseReference.Location,
                DatabaseVersion = databaseReference.DatabaseVersion,
                CreatedAt = DateTime.UtcNow,
                LastRunAt = DateTime.UtcNow,
            };

            queryHistoryProvider.Upsert(queryHistory);

            return Task.FromResult<IList<IScreen>>(result);
        }

        private static string RemoveQueryComments(string sql)
        {
            const string pattern = @"(?<=^ ([^'""] |['][^']*['] |[""][^""]*[""])*) (--.*$|/\*(.|\n)*?\*/)";
            return Regex.Replace(sql, pattern, "", RegexOptions.IgnorePatternWhitespace | RegexOptions.Multiline);
        }
    }
}