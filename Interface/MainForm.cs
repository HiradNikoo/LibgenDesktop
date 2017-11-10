﻿using System;
using System.Drawing;
using System.Windows.Forms;
using LibgenDesktop.Cache;
using LibgenDesktop.Database;
using LibgenDesktop.Infrastructure;
using LibgenDesktop.Settings;

namespace LibgenDesktop.Interface
{
    public partial class MainForm : Form
    {
        private const int MIN_BOOK_COUNT_FOR_INITIAL_LOAD_UPDATE = 20000;

        private readonly LocalDatabase localDatabase;
        private readonly DataCache dataCache;
        private AppSettings appSettings;
        private ProgressOperation currentProgressOperation;

        public MainForm()
        {
            appSettings = SettingsStorage.LoadSettings();
            localDatabase = new LocalDatabase(appSettings.DatabaseFileName);
            dataCache = new DataCache(localDatabase);
            currentProgressOperation = null;
            InitializeComponent();
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            if (appSettings.Window.Maximized)
            {
                WindowState = FormWindowState.Maximized;
            }
            else
            {
                Left = appSettings.Window.Left;
                Top = appSettings.Window.Top;
                Width = appSettings.Window.Width;
                Height = appSettings.Window.Height;
            }
            offlineModeMenuItem.Checked = appSettings.OfflineMode;
            UpdateOfflineModeStatus(false);
            Icon = IconUtils.GetAppIcon();
            mainMenu.BackColor = Color.White;
            statusPanel.BackColor = Color.White;
            StartProgressOperation(dataCache.CreateLoadBooksOperation());
            bookListView.VirtualListDataSource = dataCache.DataAccessor;
        }

        private void MainForm_Shown(object sender, EventArgs e)
        {
            searchTextBox.Focus();
        }

        private void exitMenuItem_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void openDatabaseMenuItem_Click(object sender, EventArgs e)
        {

        }

        private void importFromSqlDumpMenuItem_Click(object sender, EventArgs e)
        {
            if (openSqlDumpDialog.ShowDialog() == DialogResult.OK)
            {
                ProgressOperation importSqlDumpOperation = dataCache.CreateImportSqlDumpOperation(openSqlDumpDialog.FileName);
                ProgressForm progressForm = new ProgressForm(importSqlDumpOperation);
                progressForm.ShowDialog();
                searchTextBox.Clear();
                bookListView.VirtualListDataSource = dataCache.DataAccessor;
                ShowBookCountInStatusLabel();
                importFromSqlDumpMenuItem.Enabled = dataCache.DataAccessor.GetObjectCount() == 0;
            }
        }

        private void StartProgressOperation(ProgressOperation progressOperation)
        {
            currentProgressOperation = progressOperation;
            progressOperation.ProgressEvent += ProgressOperation_ProgressEvent;
            progressOperation.CompletedEvent += ProgressOperation_CompletedEvent;
            progressOperation.CancelledEvent += ProgressOperation_CancelledEvent;
            progressOperation.ErrorEvent += ProgressOperation_ErrorEvent;
            progressBar.Value = 0;
            progressBar.Visible = true;
            searchTextBox.ReadOnly = true;
            progressBar.Style = progressOperation.IsUnbounded ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
            progressOperation.Start();
        }

        private void ProgressOperation_ProgressEvent(object sender, ProgressEventArgs e)
        {
            BeginInvoke(new Action(() =>
            {
                bool updateBookListView = bookListView.VirtualListSize == 0;
                if (!updateBookListView)
                {
                    int newBookCount = dataCache.DataAccessor.GetObjectCount() - bookListView.VirtualListSize;
                    updateBookListView = newBookCount > MIN_BOOK_COUNT_FOR_INITIAL_LOAD_UPDATE && newBookCount > bookListView.VirtualListSize;
                }
                if (updateBookListView)
                {
                    bookListView.UpdateVirtualListSize();
                }
                if (!Double.IsNaN(e.PercentCompleted))
                {
                    progressBar.SetProgressNoAnimation((int)Math.Truncate(e.PercentCompleted * 10));
                }
                if (e.ProgressDescription != null)
                {
                    statusLabel.Text = e.ProgressDescription;
                }
            }));
        }

        private void ProgressOperation_CompletedEvent(object sender, EventArgs e)
        {
            BeginInvoke(new Action(() =>
            {
                bookListView.UpdateVirtualListSize();
                progressBar.Visible = false;
                searchTextBox.ReadOnly = false;
                ShowBookCountInStatusLabel();
                if (dataCache.IsInAllBooksMode)
                {
                    importFromSqlDumpMenuItem.Enabled = dataCache.DataAccessor.GetObjectCount() == 0;
                }
            }));
            RemoveProgressOperation();
        }

        private void ProgressOperation_CancelledEvent(object sender, EventArgs e)
        {
            BeginInvoke(new Action(() => progressBar.Visible = false));
            RemoveProgressOperation();
        }

        private void ProgressOperation_ErrorEvent(object sender, ErrorEventArgs e)
        {
            BeginInvoke(new Action(() =>
            {
                progressBar.Visible = false;
                searchTextBox.ReadOnly = false;
                ErrorForm errorForm = new ErrorForm(e.Exception.ToString());
                errorForm.ShowDialog();
            }));
            StopProgressOperation();
        }

        private void StopProgressOperation()
        {
            currentProgressOperation?.Cancel();
            RemoveProgressOperation();
        }

        private void RemoveProgressOperation()
        {
            ProgressOperation removingProgressOperation = currentProgressOperation;
            if (removingProgressOperation != null)
            {
                removingProgressOperation.ProgressEvent -= ProgressOperation_ProgressEvent;
                removingProgressOperation.CompletedEvent -= ProgressOperation_CompletedEvent;
                removingProgressOperation.CancelledEvent -= ProgressOperation_CancelledEvent;
                removingProgressOperation.ErrorEvent -= ProgressOperation_ErrorEvent;
                currentProgressOperation = null;
            }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            StopProgressOperation();
            appSettings.Window.Maximized = WindowState == FormWindowState.Maximized;
            appSettings.Window.Left = Left;
            appSettings.Window.Top = Top;
            appSettings.Window.Width = Width;
            appSettings.Window.Height = Height;
            SettingsStorage.SaveSettings(appSettings);
        }

        private void bookListView_DoubleClick(object sender, EventArgs e)
        {
            OpenSelectedBookDetails();
        }

        private void searchTextBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!searchTextBox.ReadOnly && e.KeyChar == '\r')
            {
                e.Handled = true;
                ProgressOperation searchBookOperation = dataCache.CreateSearchBooksOperation(searchTextBox.Text);
                bookListView.VirtualListDataSource = dataCache.DataAccessor;
                if (searchBookOperation != null)
                {
                    StartProgressOperation(searchBookOperation);
                }
                else
                {
                    ShowBookCountInStatusLabel();
                }
            }
        }

        private void ShowBookCountInStatusLabel()
        {
            string statusLabelPrefix = dataCache.IsInAllBooksMode ? "Всего книг" : "Найдено книг";
            statusLabel.Text = $"{statusLabelPrefix}: {dataCache.DataAccessor.GetObjectCount().ToString("N0", Formatters.BookCountFormat)}";
        }

        private void bookListView_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Up:
                case Keys.Down:
                case Keys.Left:
                case Keys.Right:
                case Keys.PageUp:
                case Keys.PageDown:
                case Keys.Home:
                case Keys.End:
                case Keys.F4:
                    return;
                case Keys.Escape:
                    searchTextBox.Focus();
                    searchTextBox.SelectAll();
                    break;
                case Keys.Enter:
                    BeginInvoke(new Action(() => OpenSelectedBookDetails()));
                    break;
            }
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        private void OpenSelectedBookDetails()
        {
            Book selectedBook = bookListView.SelectedObject as Book;
            selectedBook = localDatabase.GetBookById(selectedBook.Id);
            BookForm bookForm = new BookForm(selectedBook, appSettings.OfflineMode);
            bookForm.ShowDialog();
        }

        private void UpdateOfflineModeStatus(bool saveSettings)
        {
            bool offlineMode = offlineModeMenuItem.Checked;
            connectionStatusLabel.Visible = offlineMode;
            if (saveSettings)
            {
                appSettings.OfflineMode = offlineMode;
                SettingsStorage.SaveSettings(appSettings);
            }
        }

        private void offlineModeMenuItem_Click(object sender, EventArgs e)
        {
            UpdateOfflineModeStatus(true);
        }
    }
}
