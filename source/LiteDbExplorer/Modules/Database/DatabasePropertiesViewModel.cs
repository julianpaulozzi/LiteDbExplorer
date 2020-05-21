using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Caliburn.Micro;
using Humanizer;
using JetBrains.Annotations;
using LiteDbExplorer.Windows;
using LiteDbExplorer.Core;

namespace LiteDbExplorer.Modules.Database
{
    [Export(typeof(IDatabasePropertiesView))]
    public class DatabasePropertiesViewModel : Screen, IDatabasePropertiesView
    {
        private readonly IApplicationInteraction _applicationInteraction;
        private readonly IDatabaseInteractions _databaseInteractions;
        private int _userVersion;
        private DatabaseReference _databaseReference;

        [ImportingConstructor]
        public DatabasePropertiesViewModel(IApplicationInteraction applicationInteraction, IDatabaseInteractions databaseInteractions)
        {
            _applicationInteraction = applicationInteraction;
            _databaseInteractions = databaseInteractions;

            DisplayName = "Database Properties";
        }

        public void Init(DatabaseReference databaseReference)
        {
            _databaseReference = databaseReference;

            DisplayName = $"Database Properties - {databaseReference.Name}";

            UserVersion = _databaseReference.UserVersion;

            SetDatabaseInfo();
        }

        public IEnumerable<DisplayInfo> DatabaseInfo { get; private set; }

        public IEnumerable<DisplayInfo> DatabaseFileInfo { get; private set; }

        public string MetadataJson { get; private set; }

        public int UserVersion
        {
            get => _userVersion;
            set
            {
                _userVersion = value;
                HasChanges = true;
            }
        }

        public bool HasChanges { get; private set; }

        public bool CanAcceptButton => HasChanges;

        public void AcceptButton()
        {
            if (_databaseReference.UserVersion != UserVersion)
            {
                _databaseReference.UserVersion = UserVersion;
            }

            TryClose(true);
        }

        public bool CanCancelButton => true;

        public void CancelButton()
        {
            TryClose(false);
        }

        [UsedImplicitly]
        public async Task ShrinkDatabase()
        {
            var oldSizeInfo = DatabaseFileInfo.FirstOrDefault(p => p.HasTag("FileSize"));

            await _databaseInteractions.ShrinkDatabase(_databaseReference);

            SetDatabaseInfo();

            var newSizeInfo = DatabaseFileInfo.FirstOrDefault(p => p.HasTag("FileSize"));

            var message = $"Database {_databaseReference.Name} shrink completed.\n";
            if (oldSizeInfo != null && newSizeInfo != null)
            {
                message += $"From {oldSizeInfo.Content} to {newSizeInfo.Content}";
            }

            _applicationInteraction.ShowAlert(message, "Shrink Database", UINotificationType.Info);
        }
        
        [UsedImplicitly]
        public async Task SetPassword()
        {
            if (InputBoxWindow.ShowDialog("New password, enter empty string to remove password.", "", "", null, out string password) == true)
            {
                await _databaseInteractions.ResetPassword(_databaseReference, password);
                SetDatabaseInfo();
            }
        }

        private void SetDatabaseInfo()
        {
            var engineInfoDocument = _databaseReference.InternalDatabaseInfo();

            var databaseInfo = new List<DisplayInfo>
            {
                new DisplayInfo("Collections:", _databaseReference.Collections.Count),
                new DisplayInfo("TimeOut:", engineInfoDocument["TimeOut"].AsInt64),
                new DisplayInfo("UtcDate:", engineInfoDocument["UtcDate"].RawValue),
                new DisplayInfo("LimitSize:", engineInfoDocument["LimitSize"].AsInt64),
                new DisplayInfo("CheckpointSize:", engineInfoDocument["CheckpointSize"].RawValue)
            };
            
            DatabaseInfo = databaseInfo;

            var fileInfo = new FileInfo(_databaseReference.Location);
            var databaseFileInfo = new List<DisplayInfo>
            {
                new DisplayInfo("File name:", fileInfo.Name),
                new DisplayInfo("Location:", fileInfo.DirectoryName),
                new DisplayInfo("File size:", fileInfo.Length.Bytes().ToString("#.##")) {Tag = "FileSize"},
                new DisplayInfo("Created:", fileInfo.CreationTime),
                new DisplayInfo("Last access:", fileInfo.LastAccessTime),
                new DisplayInfo("Last write:", fileInfo.LastWriteTime)
            };

            DatabaseFileInfo = databaseFileInfo;

            MetadataJson = LiteDB.JsonSerializer.Serialize(engineInfoDocument);
        }

    }

    public class DisplayInfo : INotifyPropertyChanged
    {
        public DisplayInfo(string title, object content)
        {
            Title = title;
            Content = content;
        }

        public string Title { get; set; }
        public object Content { get; set; }
        public string Tag { get; set; }

        public bool HasTag(string tag)
        {
            return Tag != null && Tag.Equals(tag, StringComparison.OrdinalIgnoreCase);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        [UsedImplicitly]
        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}