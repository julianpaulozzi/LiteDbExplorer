using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using Caliburn.Micro;
using CSharpFunctionalExtensions;
using Enterwell.Clients.Wpf.Notifications;
using Forge.Forms;
using LiteDbExplorer.Core;
using LiteDB;
using LiteDbExplorer.Modules.Shared;
using LiteDbExplorer.Presentation;
using OfficeOpenXml;
using Serilog;

namespace LiteDbExplorer.Modules
{
    public interface IDatabaseInteractions
    {
        Task CreateAndOpenDatabase();
        Task OpenDatabase();
        Task OpenDatabase(string path, string password = "");
        Task OpenDatabases(IEnumerable<string> paths);
        Task CloseDatabase(DatabaseReference database);
        Task ShrinkDatabase(DatabaseReference database);
        Task ResetPassword(DatabaseReference database, string password);

        Task<Maybe<string>> SaveDatabaseCopyAs(DatabaseReference database);
        Task<Result<CollectionDocumentChangeEventArgs>> AddFileToDatabase(IScreen context, DatabaseReference database, string filePath = null);

        Task<Result<CollectionDocumentChangeEventArgs>> CreateDocument(IScreen context, CollectionReference collection);
        Task<Result> RemoveDocuments(IEnumerable<DocumentReference> documents);
        Task<Result> CopyDocuments(IEnumerable<DocumentReference> documents);
        Task<Maybe<DocumentReference>> OpenEditDocument(DocumentReference document);

        Task<Result<CollectionReference>> AddCollection(IScreen context, DatabaseReference database);
        Task<Result> RenameCollection(CollectionReference collection);
        Task<Result<CollectionReference>> DropCollection(CollectionReference collection);


        Task<Maybe<string>> ExportAs(IScreen context, CollectionReference collectionReference, IList<DocumentReference> selectedDocuments = null);
        Task<Maybe<string>> ExportAs(IScreen context, QueryResult queryResult, string name = "");

        Task<Result<CollectionDocumentChangeEventArgs>> ImportDataFromText(CollectionReference collection, string textData);

        // Task<Maybe<string>> ExportToJson(IJsonSerializerProvider provider, string fileName = "");
        // Task<Maybe<string>> ExportToExcel(ICollection<DocumentReference> documents, string name = "");
        // Task<Maybe<string>> ExportToJson(ICollection<DocumentReference> documents, string name = "");
        // Task<Maybe<string>> ExportStoredFiles(ICollection<DocumentReference> documents);
        // Task<Maybe<string>> ExportToCsv(ICollection<DocumentReference> documents, string name = "");
        // Task<Maybe<string>> ExportDocuments(IScreen context, ICollection<DocumentReference> documents, string name = "");

    }

    [Export(typeof(IDatabaseInteractions))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class DatabaseInteractions : IDatabaseInteractions
    {
        private static readonly ILogger Logger = Log.ForContext<DatabaseInteractions>();
        private readonly IApplicationInteraction _applicationInteraction;
        private readonly IRecentDatabaseFilesProvider _recentDatabaseFilesProvider;

        [ImportingConstructor]
        public DatabaseInteractions(IApplicationInteraction applicationInteraction, IRecentDatabaseFilesProvider recentDatabaseFilesProvider)
        {
            _applicationInteraction = applicationInteraction;
            _recentDatabaseFilesProvider = recentDatabaseFilesProvider;
        }

        public async Task CreateAndOpenDatabase()
        {
            var maybeFileName = await _applicationInteraction.ShowSaveFileDialog("New Database");
            if (maybeFileName.HasNoValue)
            {
                return;
            }

            using (var stream = new FileStream(maybeFileName.Value, System.IO.FileMode.Create))
            {
                new LiteDatabase(stream);
            }

            await OpenDatabase(maybeFileName.Value).ConfigureAwait(false);
        }

        public async Task OpenDatabase()
        {
            var maybeFileName = await _applicationInteraction.ShowOpenFileDialog("Open Database");
            if (maybeFileName.HasNoValue)
            {
                return;
            }

            try
            {
                await OpenDatabase(maybeFileName.Value).ConfigureAwait(false);
            }
            catch (Exception exc)
            {
                Logger.Error(exc, "Failed to open database: ");
                _applicationInteraction.ShowError(exc, "Failed to open database: " + exc.Message);
            }
        }

        public async Task OpenDatabases(IEnumerable<string> paths)
        {
            foreach (var path in paths)
            {
                await OpenDatabase(path).ConfigureAwait(false);
            }
        }

        public async Task OpenDatabase(string path, string password = "")
        {
            if (Store.Current.IsDatabaseOpen(path))
            {
                return;
            }

            if (!File.Exists(path))
            {
                _applicationInteraction.ShowError("Cannot open database, file not found.", "File not found");
                return;
            }

            if (ArchiveExtensions.GetDriveType(path) == DriveType.Network)
            {
                _applicationInteraction.ShowAlert("Maintaining connection to network files is not guaranteed!", "Network file", UINotificationType.Info);
            }

            try
            {
                var rememberMe = false;
                if (DatabaseReference.IsDbPasswordProtected(path))
                {
                    if (string.IsNullOrWhiteSpace(password) && _recentDatabaseFilesProvider.TryGetPassword(path, out var storedPassword))
                    {
                        password = storedPassword;
                        rememberMe = true;
                    }

                    var maybePasswordInput = await _applicationInteraction.ShowPasswordInputDialog("Database is password protected, enter password:", "Database password.", password, rememberMe);
                    if (maybePasswordInput.HasNoValue)
                    {
                        return;
                    }

                    password = maybePasswordInput.Value.Password;
                    rememberMe = maybePasswordInput.Value.RememberMe;
                }

                var connectionOptions = new DatabaseConnectionOptions(path, password)
                {
                    Mode = Properties.Settings.Default.Database_ConnectionFileMode
                };

                var databaseReference = new DatabaseReference(connectionOptions);

                Store.Current.AddDatabase(databaseReference);

                if (!string.IsNullOrEmpty(password) && rememberMe)
                {
                    _recentDatabaseFilesProvider.InsertRecentFile(databaseReference.DatabaseVersion, path, password);
                }
                else
                {
                    _recentDatabaseFilesProvider.InsertRecentFile(databaseReference.DatabaseVersion, path);
                }
            }
            catch (LiteException liteException)
            {
                await OpenDatabaseExceptionHandler(liteException, path, password);
            }
            catch (NotSupportedException notSupportedException)
            {
                _applicationInteraction.ShowError(notSupportedException, "Failed to open database [NotSupportedException]:" + Environment.NewLine + notSupportedException.Message);
            }
            catch (Exception e)
            {
                Logger.Error(e, "Failed to open database: ");
                _applicationInteraction.ShowError(e, "Failed to open database [Exception]:" + Environment.NewLine + e.Message);
            }
        }

        protected virtual async Task OpenDatabaseExceptionHandler(LiteException liteException, string path, string password = "")
        {
            _applicationInteraction.ShowError(liteException.StackTrace, liteException.Message + ". Is this a version 5 file?");
            if (liteException.Message == "Invalid password")
            {
                if (!string.IsNullOrEmpty(password))
                {
                    _applicationInteraction.ShowAlert("Failed to open database [LiteException]:" + Environment.NewLine + liteException.Message, null, UINotificationType.Error);
                }
                await OpenDatabase(path, password).ConfigureAwait(false);
            }
            else
            {
                _applicationInteraction.ShowError(liteException.StackTrace, liteException.Message + ". Is this possibly a version 5 file?");
            }
        }

        public Task CloseDatabase(DatabaseReference database)
        {
            Store.Current.CloseDatabase(database);

            return Task.CompletedTask;
        }

        public async Task ShrinkDatabase(DatabaseReference database)
        {
            await Task.Factory.StartNew(() =>
            {
                database.RebuildDatabase();
            });
        }

        public async Task ResetPassword(DatabaseReference database, string password)
        {
            await Task.Factory.StartNew(() =>
            {
                database.RebuildDatabase(string.IsNullOrEmpty(password) ? null : password);
            });

            _recentDatabaseFilesProvider.ResetPassword(database.Location, password, true);
        }

        public async Task<Maybe<string>> SaveDatabaseCopyAs(DatabaseReference database)
        {
            var databaseLocation = database.Location;
            var fileInfo = new FileInfo(databaseLocation);
            if (fileInfo.DirectoryName == null)
            {
                throw new FileNotFoundException(databaseLocation);
            }

            var newFileName = fileInfo.EnsureUniqueFileName();

            var initialDirectory = fileInfo.DirectoryName ??
                               Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            var maybeFileName = await _applicationInteraction.ShowSaveFileDialog("Save database as...", fileName: newFileName, initialDirectory: initialDirectory);
            if (maybeFileName.HasNoValue)
            {
                return Maybe<string>.None;
            }

            fileInfo.CopyTo(maybeFileName.Value, false);

            return Maybe<string>.From(maybeFileName.Value);
        }

        public async Task<Result<CollectionDocumentChangeEventArgs>> AddFileToDatabase(IScreen context, DatabaseReference database, string filePath = null)
        {
            Maybe<string> maybeFileName = filePath;
            if (string.IsNullOrEmpty(filePath))
            {
                maybeFileName = await _applicationInteraction.ShowOpenFileDialog("Add file to database");
                if (maybeFileName.HasNoValue)
                {
                    return Result.Failure<CollectionDocumentChangeEventArgs>(Fails.Canceled);
                }
            }

            var exportOptions = new AddFileOptions(database, Path.GetFileName(maybeFileName.Value));
            var optionsResult = await ShowHostDialog(context).For(exportOptions);
            if (optionsResult.Action is "cancel")
            {
                return Result.Failure<CollectionDocumentChangeEventArgs>(Fails.Canceled);
            }

            try
            {
                var fileId = optionsResult.Model.NewFileId;
                if (!string.IsNullOrEmpty(fileId))
                {
                    var file = database.AddFile(fileId, maybeFileName.Value);
                    var documentsCreated = new CollectionDocumentChangeEventArgs(ReferenceNodeChangeAction.Add, new[] { file }, file.Collection);
                    return Result.Ok(documentsCreated);
                }
            }
            catch (Exception exc)
            {
                _applicationInteraction.ShowError(exc, "Failed to upload file:" + Environment.NewLine + exc.Message, "Database error");
            }


            return Result.Failure<CollectionDocumentChangeEventArgs>("FILE_OPEN_FAIL");
        }

        public Task<Result> RemoveDocuments(IEnumerable<DocumentReference> documents)
        {
            if (!_applicationInteraction.ShowConfirm("Are you sure you want to remove items?", "Are you sure?"))
            {
                return Task.FromResult(Result.Failure(Fails.Canceled));
            }

            foreach (var document in documents.ToList())
            {
                document.RemoveSelf();
            }

            return Task.FromResult(Result.Ok());
        }

        public async Task<Result<CollectionReference>> AddCollection(IScreen context, DatabaseReference database)
        {
            var exportOptions = new AddCollectionOptions(database);
            var optionsResult = await ShowHostDialog(context).For(exportOptions);
            if (optionsResult.Action is "cancel")
            {
                return Result.Failure<CollectionReference>(Fails.Canceled);
            }

            try
            {
                var collectionName = optionsResult.Model.NewCollectionName;
                if (!string.IsNullOrEmpty(collectionName))
                {
                    var collectionReference = database.AddCollection(collectionName);
                    return Result.Ok(collectionReference);
                }

                return Result.Ok<CollectionReference>(null);
            }
            catch (Exception exc)
            {
                var message = "Failed to add new collection:" + Environment.NewLine + exc.Message;
                _applicationInteraction.ShowError(exc, message, "Database error");
                return Result.Failure<CollectionReference>(message);
            }
        }

        public async Task<Result> RenameCollection(CollectionReference collection)
        {
            try
            {
                var currentName = collection.Name;

                Result Validate(string value)
                {
                    if (string.IsNullOrEmpty(value))
                    {
                        return Result.Failure("Name cannot be empty.");
                    }

                    if (value.Any(char.IsWhiteSpace))
                    {
                        return Result.Failure("Name can not contain white spaces.");
                    }

                    if (collection.Database.ContainsCollection(value))
                    {
                        return Result.Failure($"Collection \"{value}\" already exists!");
                    }

                    return Result.Ok();
                }

                var maybeName = await _applicationInteraction
                    .ShowInputDialog("New collection name:", "Enter new collection name", currentName, Validate);

                if (maybeName.HasNoValue)
                {
                    return Result.Failure(Fails.Canceled);
                }

                collection.Database.RenameCollection(currentName, maybeName.Value);
                return Result.Ok();

            }
            catch (Exception exc)
            {
                var message = "Failed to rename collection:" + Environment.NewLine + exc.Message;
                _applicationInteraction.ShowError(exc, message, "Database error");
                return Result.Failure(message);
            }
        }

        public Task<Result<CollectionReference>> DropCollection(CollectionReference collection)
        {
            try
            {
                var collectionName = collection.Name;
                if (_applicationInteraction.ShowConfirm($"Are you sure you want to drop collection \"{collectionName}\"?", "Are you sure?"))
                {
                    collection.Database.DropCollection(collectionName);

                    return Task.FromResult(Result.Ok(collection));
                }

                return Task.FromResult(Result.Failure<CollectionReference>(Fails.Canceled));
            }
            catch (Exception exc)
            {
                var message = "Failed to drop collection:" + Environment.NewLine + exc.Message;
                _applicationInteraction.ShowError(exc, message, "Database error");
                return Task.FromResult(Result.Failure<CollectionReference>(message));
            }
        }

        public async Task<Maybe<string>> ExportAs(
            IScreen context,
            CollectionReference collectionReference,
            IList<DocumentReference> selectedDocuments = null)
        {
            if (collectionReference == null)
            {
                return null;
            }

            var exportOptions = new CollectionExportOptions(collectionReference.IsFilesOrChunks, selectedDocuments?.Count);
            var result = await ShowHostDialog(context).For(exportOptions);
            if (result.Action is "cancel")
            {
                return null;
            }

            var itemsToExport = result.Model.GetSelectedRecordsFilter() == 0
                ? collectionReference.Items
                : selectedDocuments;
            var referenceName = collectionReference.Name;

            Maybe<string> maybePath = null;
            switch (result.Model.GetSelectedExportFormat())
            {
                case 0:
                    maybePath = await ExportToJson(itemsToExport, referenceName);
                    break;
                case 1:
                    maybePath = await ExportToExcel(itemsToExport, referenceName);
                    break;
                case 2:
                    maybePath = await ExportToCsv(itemsToExport, referenceName);
                    break;
                case 3:
                    maybePath = await ExportStoredFiles(itemsToExport);
                    break;
            }

            if (maybePath.HasValue)
            {
                var builder = NotificationInteraction.Default()
                    .HasMessage($"{result.Model.ExportFormat} saved in:\n{maybePath.Value.ShrinkPath(128)}");

                if (Path.HasExtension(maybePath.Value))
                {
                    builder.Dismiss().WithButton("Open",
                        async button =>
                        {
                            await _applicationInteraction.OpenFileWithAssociatedApplication(maybePath.Value);
                        });
                }

                builder.WithButton("Reveal in Explorer",
                        async button => { await _applicationInteraction.RevealInExplorer(maybePath.Value); })
                    .Dismiss().WithButton("Close", button => { });

                builder.Queue();
            }

            return maybePath;
        }


        public async Task<Maybe<string>> ExportAs(
            IScreen context,
            QueryResult queryResult,
            string name = "")
        {
            if (queryResult == null)
            {
                return null;
            }

            var exportOptions = new CollectionExportOptions(false, null);
            var result = await ShowHostDialog(context).For(exportOptions);
            if (result.Action is "cancel")
            {
                return null;
            }

            Maybe<string> maybePath = null;
            switch (result.Model.GetSelectedExportFormat())
            {
                case 0:
                    maybePath = await ExportToJson(queryResult, name);
                    break;
                case 1:
                    maybePath = await ExportToExcel(queryResult.DataTable, name);
                    break;
                case 2:
                    maybePath = await ExportToCsv(queryResult.DataTable, name);
                    break;
            }

            if (maybePath.HasValue)
            {
                var builder = NotificationInteraction.Default()
                    .HasMessage($"{result.Model.ExportFormat} saved in:\n{maybePath.Value.ShrinkPath(128)}");

                if (Path.HasExtension(maybePath.Value))
                {
                    builder.Dismiss().WithButton("Open",
                        async button =>
                        {
                            await _applicationInteraction.OpenFileWithAssociatedApplication(maybePath.Value);
                        });
                }

                builder.WithButton("Reveal in Explorer",
                        async button => { await _applicationInteraction.RevealInExplorer(maybePath.Value); })
                    .Dismiss().WithButton("Close", button => { });

                builder.Queue();
            }

            return maybePath;
        }

        public Task<Result> CopyDocuments(IEnumerable<DocumentReference> documents)
        {
            var documentAggregator = new DocumentReferenceAggregator(documents);

            Clipboard.SetData(DataFormats.Text, documentAggregator.Serialize());

            return Task.FromResult(Result.Ok());
        }

        public Task<Maybe<DocumentReference>> OpenEditDocument(DocumentReference document)
        {
            var result = _applicationInteraction.OpenEditDocument(document);
            return Task.FromResult(Maybe<DocumentReference>.From(result ? document : null));
        }

        public Task<Result<CollectionDocumentChangeEventArgs>> ImportDataFromText(CollectionReference collection, string textData)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(textData))
                {
                    return Task.FromResult(Result.Ok(CollectionDocumentChangeEventArgs.Nome));
                }

                var newValue = JsonSerializer.Deserialize(textData);
                var newDocs = new List<DocumentReference>();
                if (newValue.IsArray)
                {
                    foreach (var value in newValue.AsArray)
                    {
                        var doc = value.AsDocument;
                        var documentReference = collection.AddDocument(doc);
                        newDocs.Add(documentReference);
                    }
                }
                else
                {
                    var doc = newValue.AsDocument;
                    var documentReference = collection.AddDocument(doc);
                    newDocs.Add(documentReference);
                }

                var documentsUpdate = new CollectionDocumentChangeEventArgs(ReferenceNodeChangeAction.Add, newDocs, collection);

                return Task.FromResult(Result.Ok(documentsUpdate));
            }
            catch (Exception e)
            {
                var message = "Failed to import document from text content: " + e.Message;
                Logger.Warning(e, "Cannot process clipboard data.");
                _applicationInteraction.ShowError(e, message, "Import Error");

                return Task.FromResult(Result.Failure<CollectionDocumentChangeEventArgs>(message));
            }
        }

        public async Task<Result<CollectionDocumentChangeEventArgs>> CreateDocument(IScreen context, CollectionReference collection)
        {
            if (collection is FileCollectionReference)
            {
                return await AddFileToDatabase(context, collection.Database);
            }

            var addDocumentOptions = new AddDocumentOptions(collection);

            var optionsResult = await ShowHostDialog(context).For(addDocumentOptions);

            if (optionsResult.Action is AddDocumentOptions.ACTION_CANCEL)
            {
                return Result.Failure<CollectionDocumentChangeEventArgs>(Fails.Canceled);
            }

            var newId = optionsResult.Model.NewId;
            var newDoc = new BsonDocument
            {
                ["_id"] = newId
            };

            var documentReference = collection.AddDocument(newDoc);

            var documentsCreated = new CollectionDocumentChangeEventArgs(ReferenceNodeChangeAction.Add, documentReference, collection)
            {
                PostAction = (optionsResult.Model.EditAfterCreate || optionsResult.Action is AddDocumentOptions.ACTION_OK_AND_EDIT) ? "edit" : null
            };

            return Result.Ok(documentsCreated);
        }

        private async Task<Maybe<string>> ExportToJson(ICollection<DocumentReference> documents, string name = "")
        {
            var fileName = ArchiveExtensions.EnsureFileName(name, "export", ".json", true);
            var maybeJsonFileName = await _applicationInteraction.ShowSaveFileDialog("Save Json export", "Json File|*.json", fileName);
            if (maybeJsonFileName.HasValue)
            {
                if (documents.Count == 1)
                {
                    using (var writer = new StreamWriter(maybeJsonFileName.Value))
                    {
                        documents.First().Serialize(writer);
                    }
                }
                else
                {
                    var documentAggregator = new DocumentReferenceAggregator(documents);
                    using (var writer = new StreamWriter(maybeJsonFileName.Value))
                    {
                        documentAggregator.Serialize(writer);
                    }
                }
            }

            return maybeJsonFileName;
        }

        private static readonly Dictionary<BsonType, Func<object, string>> BsonTypeToExcelNumberFormat =
            new Dictionary<BsonType, Func<object, string>>
            {
                {BsonType.Int32, _ => "0"},
                {BsonType.Int64, _ => "0"},
                {BsonType.DateTime, _ => $"{CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern} {CultureInfo.CurrentCulture.DateTimeFormat.LongTimePattern}"}
            };

        private async Task<Maybe<string>> ExportToExcel(ICollection<DocumentReference> documents, string name = "")
        {
            var fileName = ArchiveExtensions.EnsureFileName(name, "export", ".xlsx", true);
            var maybeFileName = await _applicationInteraction.ShowSaveFileDialog("Save Excel export", "Excel File|*.xlsx", fileName);
            if (maybeFileName.HasNoValue)
            {
                return null;
            }

            var keys = documents.SelectAllDistinctKeys().ToArray();

            var excelPackage = new ExcelPackage();
            var ws = excelPackage.Workbook.Worksheets.Add(name);

            // Add headers
            for (var i = 0; i < keys.Length; i++)
            {
                ws.Cells[1, i + 1].Value = keys[i];
            }

            // Add data
            var currentColl = 1;
            var currentRow = 2;
            foreach (var documentReference in documents)
            {
                foreach (var key in keys)
                {
                    if (documentReference.TryGetValue(key, out var bsonValue))
                    {
                        object cellValue = null;
                        if (bsonValue.IsArray || bsonValue.IsDocument)
                        {
                            cellValue = bsonValue.ToDisplayValue();
                        }
                        else
                        {
                            cellValue = bsonValue.RawValue;
                        }

                        var cell = ws.Cells[currentRow, currentColl];
                        cell.Value = cellValue;
                        if (BsonTypeToExcelNumberFormat.TryGetValue(bsonValue.Type, out var format))
                        {
                            cell.Style.Numberformat.Format = format(bsonValue);
                        }
                    }

                    currentColl++;
                }

                currentColl = 1;
                currentRow++;
            }

            var tableRange = ws.Cells[1, 1, documents.Count + 1, keys.Length];
            var resultsTable = ws.Tables.Add(tableRange, $"{Regex.Replace(name, @"\s", "_")}_table");

            resultsTable.ShowFilter = true;
            resultsTable.ShowHeader = true;

            // AutoFit
            ws.Cells[ws.Dimension.Address].AutoFitColumns();

            excelPackage.SaveAs(new FileInfo(maybeFileName.Value));
            excelPackage.Dispose();

            return maybeFileName.Value;
        }

        private async Task<Maybe<string>> ExportToExcel(DataTable dataTable, string name = "")
        {
            var fileName = ArchiveExtensions.EnsureFileName(name, "export", ".xlsx", true);
            var maybeFileName = await _applicationInteraction.ShowSaveFileDialog("Save Excel export", "Excel File|*.xlsx", fileName);
            if (maybeFileName.HasNoValue)
            {
                return null;
            }

            var excelPackage = new ExcelPackage();
            var ws = excelPackage.Workbook.Worksheets.Add(name);

            ws.Cells[@"A1"].LoadFromDataTable(dataTable, true);

            var resultsTable = ws.Tables.Add(ws.Dimension, $"{Regex.Replace(name, @"\s", "_")}_table");

            resultsTable.ShowFilter = true;
            resultsTable.ShowHeader = true;

            // AutoFit
            ws.Cells[ws.Dimension.Address].AutoFitColumns();

            excelPackage.SaveAs(new FileInfo(maybeFileName.Value));
            excelPackage.Dispose();

            return maybeFileName.Value;
        }

        private async Task<Maybe<string>> ExportToCsv(ICollection<DocumentReference> documents, string name = "")
        {
            var fileName = ArchiveExtensions.EnsureFileName(name, "export", ".csv", true);
            var maybeFileName = await _applicationInteraction.ShowSaveFileDialog("Save CSV export", "Excel File|*.xlsx", fileName);
            if (maybeFileName.HasNoValue)
            {
                return null;
            }

            var separator = ",";
            var reservedTokens = new[] { '\"', ',', '\n', '\r' };
            var keys = documents.SelectAllDistinctKeys().ToArray();

            var contents = new List<string>
            {
                string.Join(separator, keys)
            };

            string NormalizeValue(BsonValue value)
            {
                string s = null;
                if (!value.IsArray && !value.IsDocument && !value.IsNull)
                {
                    s = Convert.ToString(value.RawValue, CultureInfo.InvariantCulture);
                }

                // Escape reserved tokens
                if (s != null && s.IndexOfAny(reservedTokens) >= 0)
                {
                    s = "\"" + s.Replace("\"", "\"\"") + "\"";
                }

                return s;
            }

            foreach (var documentReference in documents)
            {
                var rowCols = new string[keys.Length];
                var currentCol = 0;
                foreach (var key in keys)
                {
                    if (documentReference.TryGetValue(key, out var bsonValue))
                    {
                        rowCols[currentCol] = NormalizeValue(bsonValue);
                    }
                    currentCol++;
                }
                contents.Add(string.Join(separator, rowCols));
            }

            File.WriteAllLines(maybeFileName.Value, contents, Encoding.UTF8);

            return maybeFileName;

        }

        private async Task<Maybe<string>> ExportToCsv(DataTable dataTable, string name = "")
        {
            var fileName = ArchiveExtensions.EnsureFileName(name, "export", ".csv", true);
            var maybeFileName = await _applicationInteraction.ShowSaveFileDialog("Save CSV export", "CSV File|*.csv", fileName);
            if (maybeFileName.HasNoValue)
            {
                return null;
            }

            var separator = ",";
            var reservedTokens = new[] { '\"', ',', '\n', '\r' };
            var columnNames = dataTable.Columns
                .Cast<DataColumn>()
                .Select(column => column.ColumnName);

            var contents = new List<string>
            {
                string.Join(separator, columnNames)
            };

            string NormalizeValue(object field)
            {
                var value = Convert.ToString(field, CultureInfo.InvariantCulture);
                if (value != null && value.IndexOfAny(reservedTokens) >= 0)
                {
                    value = "\"" + value.Replace("\"", "\"\"") + "\"";
                }
                return value;
            }

            foreach (DataRow row in dataTable.Rows)
            {
                var fields = row.ItemArray.Select(NormalizeValue);

                contents.Add(string.Join(separator, fields));
            }

            File.WriteAllLines(maybeFileName.Value, contents, Encoding.UTF8);

            return maybeFileName;

        }

        private async Task<Maybe<string>> ExportStoredFiles(ICollection<DocumentReference> documents)
        {
            var fileDocuments = documents.OfType<FileDocumentReference>().ToArray();
            if (!fileDocuments.Any())
            {
                return null;
            }

            Maybe<string> maybePath;
            if (documents.Count == 1)
            {
                var file = fileDocuments[0];
                maybePath = await _applicationInteraction.ShowSaveFileDialog(fileName: file.Filename);
                if (maybePath.HasValue)
                {
                    file.SaveFile(maybePath.Value);
                }
            }
            else
            {
                maybePath = await _applicationInteraction.ShowFolderPickerDialog("Select folder to export files to...");
                if (maybePath.HasValue)
                {
                    foreach (var file in fileDocuments)
                    {
                        var prefix = file.GetIdAsFilename();

                        var path = Path.Combine(maybePath.Value, $"{prefix}-{file.Filename}");

                        ArchiveExtensions.EnsureFileDirectory(path);

                        file.SaveFile(path);
                    }
                }
            }

            return maybePath;
        }

        private async Task<Maybe<string>> ExportToJson(IJsonSerializerProvider provider, string fileName = "")
        {
            var exportFileName = "export.json";
            if (!string.IsNullOrEmpty(fileName))
            {
                exportFileName = ArchiveExtensions.EnsureFileNameExtension(fileName, ".json");
            }

            var maybeFileName = await _applicationInteraction.ShowSaveFileDialog("Save Json export", "Json File|*.json", exportFileName);
            if (maybeFileName.HasValue)
            {
                using (var writer = new StreamWriter(maybeFileName.Value))
                {
                    provider.Serialize(writer);
                }
            }

            return maybeFileName;
        }

        private IModelHost ShowHostDialog(IScreen context)
        {
            var dialogHostIdentifier = GetDialogHostIdentifier(context);
            return Show.Dialog(dialogHostIdentifier);
        }

        private string GetDialogHostIdentifier(IScreen context)
        {
            var dialogHostIdentifier = AppConstants.DialogHosts.Shell;
            if (context is Wpf.Framework.Shell.IDocument document)
            {
                dialogHostIdentifier = document.DialogHostIdentifier;
            }

            return dialogHostIdentifier;
        }
    }
}