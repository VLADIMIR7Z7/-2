using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using Microsoft.Win32;

namespace TextEditor
{
    public partial class MainWindow : Window
    {
        private string currentFilePath = null;
        private bool isTextChanged = false;
        private FStringScanner scanner;

        public class Token
        {
            public int Code { get; set; }
            public string Type { get; set; }
            public string Value { get; set; }
            public int Line { get; set; }
            public int StartPosition { get; set; }
            public int EndPosition { get; set; }
            public bool IsError { get; set; }
            public string ErrorMessage { get; set; }
        }

        public class FStringScanner
        {
            private string _input;
            private int _position;
            private int _line;
            private int _lineStart;
            private char _current;
            private List<Token> _tokens;

            public List<Token> Scan(string input)
            {
                _input = input ?? "";
                _position = 0;
                _line = 1;
                _lineStart = 0;
                _tokens = new List<Token>();

                if (_input.Length > 0) _current = _input[0];

                while (_position < _input.Length)
                {
                    if (char.IsWhiteSpace(_current))
                    {
                        HandleWhitespace();
                        continue;
                    }

                    if (!TryParseFString())
                    {
                        AddError($"Недопустимый символ '{_current}'", _line, Pos());
                        Advance();
                    }
                }
                return _tokens;
            }

            private void Advance()
            {
                _position++;
                if (_position < _input.Length) _current = _input[_position];
                else _current = '\0';
            }

            private int Pos() => _position - _lineStart + 1;

            private void HandleWhitespace()
            {
                int start = Pos();
                while (_position < _input.Length && char.IsWhiteSpace(_current))
                {
                    if (_current == '\n')
                    {
                        _line++;
                        _lineStart = _position + 1;
                    }
                    Advance();
                }
            }

            private bool TryParseFString()
            {
                int savedPos = _position;
                int savedLine = _line;
                int savedStart = _lineStart;

                if (!ParseChar('f', 2, "ключевое слово f")) return false;
                if (!ParseChar('"', 6, "кавычка")) return false;
                if (!ParseChar('{', 4, "открывающая скобка")) return false;
                if (!ParseChar('m', 3, "идентификатор")) return false;
                if (!ParseChar(':', 8, "разделитель")) return false;
                if (!ParseChar('.', 8, "точка")) return false;
                if (!ParseDigits()) return false;
                if (!ParseExponent()) return false;
                if (!ParseChar('}', 5, "закрывающая скобка")) return false;
                if (!ParseChar('"', 7, "кавычка")) return false;

                return true;
            }

            private bool ParseChar(char expected, int code, string desc)
            {
                if (_current == expected)
                {
                    _tokens.Add(new Token
                    {
                        Code = code,
                        Type = desc,
                        Value = _current.ToString(),
                        Line = _line,
                        StartPosition = Pos(),
                        EndPosition = Pos()
                    });
                    Advance();
                    return true;
                }
                return false;
            }

            private bool ParseDigits()
            {
                if (!char.IsDigit(_current)) return false;

                int start = Pos();
                string digits = "";
                while (_position < _input.Length && char.IsDigit(_current))
                {
                    digits += _current;
                    Advance();
                }

                _tokens.Add(new Token
                {
                    Code = 8,
                    Type = "цифра",
                    Value = digits,
                    Line = _line,
                    StartPosition = start,
                    EndPosition = start + digits.Length - 1
                });
                return true;
            }

            private bool ParseExponent()
            {
                if (_current == 'e' || _current == 'E')
                {
                    _tokens.Add(new Token
                    {
                        Code = 9,
                        Type = "экспонента",
                        Value = _current.ToString(),
                        Line = _line,
                        StartPosition = Pos(),
                        EndPosition = Pos()
                    });
                    Advance();
                    return true;
                }
                return false;
            }

            private void AddError(string msg, int line, int col)
            {
                _tokens.Add(new Token
                {
                    Code = 999,
                    Type = "ОШИБКА",
                    Value = _current.ToString(),
                    Line = line,
                    StartPosition = col,
                    EndPosition = col,
                    IsError = true,
                    ErrorMessage = msg
                });
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            scanner = new FStringScanner();
            InitializeNewDocument();
            ResultsGrid.ItemsSource = new List<Token>();
        }

        private void InitializeNewDocument()
        {
            EditorBox.Document = new FlowDocument();
            EditorBox.Focus();
            UpdateStatusBar();
        }

        private void CreateFile_Click(object sender, RoutedEventArgs e)
        {
            if (PromptSaveChanges())
            {
                EditorBox.Document = new FlowDocument();
                currentFilePath = null;
                isTextChanged = false;
                UpdateStatusBar();
                StatusText.Text = "Создан новый документ";
            }
        }

        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            if (!PromptSaveChanges()) return;

            OpenFileDialog openDialog = new OpenFileDialog
            {
                Filter = "Текстовые файлы (*.txt)|*.txt|Rich Text Files (*.rtf)|*.rtf|Все файлы (*.*)|*.*",
                Title = "Открыть файл"
            };

            if (openDialog.ShowDialog() == true)
            {
                try
                {
                    TextRange range = new TextRange(EditorBox.Document.ContentStart, EditorBox.Document.ContentEnd);
                    using (FileStream fs = new FileStream(openDialog.FileName, FileMode.Open, FileAccess.Read))
                    {
                        if (openDialog.FileName.EndsWith(".rtf", StringComparison.OrdinalIgnoreCase))
                            range.Load(fs, DataFormats.Rtf);
                        else
                            range.Load(fs, DataFormats.Text);
                    }

                    currentFilePath = openDialog.FileName;
                    isTextChanged = false;
                    FileInfoText.Text = Path.GetFileName(currentFilePath);
                    StatusText.Text = $"Файл загружен: {Path.GetFileName(currentFilePath)}";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка при открытии файла: {ex.Message}", "Ошибка",
                                  MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void SaveFile_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(currentFilePath))
                SaveAsFile_Click(sender, e);
            else
                SaveFile(currentFilePath);
        }

        private void SaveAsFile_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog saveDialog = new SaveFileDialog
            {
                Filter = "Текстовые файлы (*.txt)|*.txt|Rich Text Files (*.rtf)|*.rtf|Все файлы (*.*)|*.*",
                Title = "Сохранить файл как"
            };

            if (saveDialog.ShowDialog() == true)
                SaveFile(saveDialog.FileName);
        }

        private void SaveFile(string filePath)
        {
            try
            {
                TextRange range = new TextRange(EditorBox.Document.ContentStart, EditorBox.Document.ContentEnd);
                using (FileStream fs = new FileStream(filePath, FileMode.Create))
                {
                    if (filePath.EndsWith(".rtf", StringComparison.OrdinalIgnoreCase))
                        range.Save(fs, DataFormats.Rtf);
                    else
                        range.Save(fs, DataFormats.Text);
                }

                currentFilePath = filePath;
                isTextChanged = false;
                FileInfoText.Text = Path.GetFileName(currentFilePath);
                StatusText.Text = "Файл сохранен";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при сохранении файла: {ex.Message}", "Ошибка",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            if (PromptSaveChanges())
                Application.Current.Shutdown();
        }

        private bool PromptSaveChanges()
        {
            if (!isTextChanged) return true;

            var result = MessageBox.Show("Сохранить изменения в файле?", "Сохранение",
                                        MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                SaveFile_Click(null, null);
                return true;
            }
            return result != MessageBoxResult.Cancel;
        }

        private void Undo_Click(object sender, RoutedEventArgs e)
        {
            if (EditorBox.CanUndo)
            {
                EditorBox.Undo();
                StatusText.Text = "Отмена действия";
            }
        }

        private void Redo_Click(object sender, RoutedEventArgs e)
        {
            if (EditorBox.CanRedo)
            {
                EditorBox.Redo();
                StatusText.Text = "Повтор действия";
            }
        }

        private void Cut_Click(object sender, RoutedEventArgs e)
        {
            EditorBox.Cut();
            StatusText.Text = "Текст вырезан";
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            EditorBox.Copy();
            StatusText.Text = "Текст скопирован";
        }

        private void Paste_Click(object sender, RoutedEventArgs e)
        {
            EditorBox.Paste();
            StatusText.Text = "Текст вставлен";
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            EditorBox.Selection.Text = string.Empty;
            StatusText.Text = "Текст удален";
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            EditorBox.SelectAll();
            StatusText.Text = "Весь текст выделен";
        }

        private void TaskDescription_Click(object sender, RoutedEventArgs e)
        {
            ShowInfoWindow("Постановка задачи",
                "Разработать лексический анализатор для f-строки f\"{m:.Ne}\".\n\n" +
                "Основные функции:\n" +
                "• Выделение лексем\n" +
                "• Классификация по типам\n" +
                "• Обнаружение ошибок\n\n" +
                "Коды лексем:\n" +
                "2 - ключевое слово f\n" +
                "3 - идентификатор m\n" +
                "4 - открывающая скобка {\n" +
                "5 - закрывающая скобка }\n" +
                "6-7 - кавычки\n" +
                "8 - форматирование (.:цифры)\n" +
                "9 - символ экспоненты e");
        }

        private void Grammar_Click(object sender, RoutedEventArgs e)
        {
            ShowInfoWindow("Грамматика",
                "Диаграмма состояний:\n\n" +
                "start → f → \" → { → m → : → . → N → e → } → \" → выход\n\n" +
                "где N - одна или несколько цифр\n\n" +
                "Допустимые строки:\n" +
                "• f\"{m:.2e}\"\n" +
                "• f\"{m:.10e}\"\n" +
                "• f\"{m:.5e}\"");
        }

        private void GrammarClassification_Click(object sender, RoutedEventArgs e)
        {
            ShowInfoWindow("Классификация",
                "Тип: регулярная грамматика\n" +
                "Класс по Хомскому: Тип 3\n\n" +
                "Распознается конечным автоматом\n" +
                "Детерминированный автомат\n" +
                "7 состояний");
        }

        private void AnalysisMethod_Click(object sender, RoutedEventArgs e)
        {
            ShowInfoWindow("Метод анализа",
                "Конечный автомат с управлением по состояниям\n\n" +
                "Алгоритм:\n" +
                "1. Чтение символа\n" +
                "2. Проверка допустимости\n" +
                "3. Переход в новое состояние\n" +
                "4. Фиксация лексемы\n\n" +
                "Ошибка - недопустимый переход");
        }

        private void TestExample_Click(object sender, RoutedEventArgs e)
        {
            ShowInfoWindow("Тестовый пример",
                "Вход: f\"{m:.2e}\"\n\n" +
                "Результат:\n" +
                "f (ключ. слово) код 2\n" +
                "\" (кавычка) код 6\n" +
                "{ (откр. скобка) код 4\n" +
                "m (идентификатор) код 3\n" +
                ": (разделитель) код 8\n" +
                ". (точка) код 8\n" +
                "2 (цифра) код 8\n" +
                "e (экспонента) код 9\n" +
                "} (закр. скобка) код 5\n" +
                "\" (кавычка) код 7");
        }

        private void References_Click(object sender, RoutedEventArgs e)
        {
            ShowInfoWindow("Литература",
                "1. Ахо А., Лам М., Сети Р., Ульман Д. Компиляторы: принципы, технологии и инструментарий. – М.: Вильямс, 2018.\n\n" +
                "2. Вирт Н. Построение компиляторов. – М.: ДМК Пресс, 2010.\n\n" +
                "3. Хопкрофт Д., Мотвани Р., Ульман Дж. Введение в теорию автоматов, языков и вычислений. – М.: Вильямс, 2017.");
        }

        private void SourceCode_Click(object sender, RoutedEventArgs e)
        {
            ShowInfoWindow("Исходный код",
                "Структура:\n\n" +
                "• MainWindow.xaml - интерфейс\n" +
                "• MainWindow.xaml.cs - логика и сканер\n" +
                "• Встроенный класс Token\n" +
                "• Встроенный класс FStringScanner\n\n" +
                "Сканер реализует диаграмму состояний для f\"{m:.Ne}\"");
        }

        private void ShowInfoWindow(string title, string content)
        {
            Window infoWindow = new Window
            {
                Title = title,
                Content = new TextBox
                {
                    Text = content,
                    IsReadOnly = true,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(10),
                    FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                    Background = System.Windows.Media.Brushes.White,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                },
                Width = 550,
                Height = 450,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this
            };
            infoWindow.ShowDialog();
        }

        private void StartAnalysis_Click(object sender, RoutedEventArgs e)
        {
            StatusText.Text = "Лексический анализ...";

            TextRange range = new TextRange(EditorBox.Document.ContentStart, EditorBox.Document.ContentEnd);
            string text = range.Text;

            try
            {
                var tokens = scanner.Scan(text);
                ResultsGrid.ItemsSource = tokens;

                bool hasErrors = false;
                foreach (var t in tokens)
                {
                    if (t.IsError)
                    {
                        hasErrors = true;
                        break;
                    }
                }

                StatusText.Text = hasErrors ? "Обнаружены ошибки" : "Анализ завершен";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}");
                StatusText.Text = "Ошибка анализа";
            }
        }

        private void Help_Click(object sender, RoutedEventArgs e)
        {
            string help =
                "ГОРЯЧИЕ КЛАВИШИ:\n" +
                "Ctrl+N - новый файл\n" +
                "Ctrl+O - открыть\n" +
                "Ctrl+S - сохранить\n" +
                "F5 - запуск анализа\n" +
                "F1 - справка\n\n" +
                "ФОРМАТ СТРОКИ:\n" +
                "f\"{m:.Ne}\" - форматирование числа\n" +
                "N - одна или несколько цифр\n\n" +
                "ПРИМЕРЫ:\n" +
                "f\"{m:.2e}\" - корректно\n" +
                "f\"{m:.5e}\" - корректно\n" +
                "f\"{m:.x}\"  - ошибка";

            ShowInfoWindow("Справка", help);
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            string about =
                "╔════════════════════════════╗\n" +
                "║ ЛАБОРАТОРНАЯ РАБОТА №2    ║\n" +
                "║ Лексический анализатор     ║\n" +
                "╚════════════════════════════╝\n\n" +
                "Вариант: f\"{m:.Ne}\"\n\n" +
                "Автор: Петрухно В.К.\n" +
                "Группа: АП-327\n\n" +
                "Дата: 2026\n\n" +
                "Функции:\n" +
                "✓ Редактор текста\n" +
                "✓ Сканер f-строк\n" +
                "✓ Таблица лексем\n" +
                "✓ Навигация по ошибкам";

            ShowInfoWindow("О программе", about);
        }

        private void ResultsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ResultsGrid.SelectedItem is Token token && token.IsError)
            {
                TextPointer pointer = EditorBox.Document.ContentStart;
                for (int i = 1; i < token.Line; i++)
                {
                    pointer = pointer.GetLineStartPosition(1);
                    if (pointer == null) break;
                }

                if (pointer != null)
                {
                    for (int i = 1; i < token.StartPosition; i++)
                    {
                        pointer = pointer.GetNextInsertionPosition(LogicalDirection.Forward);
                        if (pointer == null) break;
                    }

                    if (pointer != null)
                    {
                        EditorBox.CaretPosition = pointer;
                        EditorBox.Focus();
                        EditorBox.ScrollToVerticalOffset(EditorBox.VerticalOffset + token.Line * 20);
                        StatusText.Text = $"Переход к ошибке: строка {token.Line}, позиция {token.StartPosition}";
                    }
                }
            }
        }

        private void EditorBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            isTextChanged = true;
            UpdateStatusBar();
        }

        private void EditorBox_SelectionChanged(object sender, RoutedEventArgs e)
        {
            UpdateStatusBar();
        }

        private void UpdateStatusBar()
        {
            try
            {
                TextPointer caret = EditorBox.CaretPosition;
                if (caret != null)
                {
                    int line = 1;
                    TextPointer ptr = caret.GetLineStartPosition(0);
                    while (ptr != null)
                    {
                        TextPointer prev = ptr.GetLineStartPosition(-1);
                        if (prev == null || prev.CompareTo(ptr) == 0) break;
                        ptr = prev;
                        line++;
                    }

                    int col = 1;
                    if (ptr != null)
                    {
                        TextPointer start = ptr;
                        TextPointer cur = start;
                        while (cur != null && cur.CompareTo(caret) < 0)
                        {
                            col++;
                            cur = cur.GetNextInsertionPosition(LogicalDirection.Forward);
                        }
                    }

                    CursorPositionText.Text = $"Стр: {line}, Стб: {col}";
                }

                FileInfoText.Text = string.IsNullOrEmpty(currentFilePath) ?
                    "Новый файл" : Path.GetFileName(currentFilePath);
                if (isTextChanged && !FileInfoText.Text.EndsWith("*"))
                    FileInfoText.Text += "*";
            }
            catch
            {
                CursorPositionText.Text = "Стр: 1, Стб: 1";
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!PromptSaveChanges())
                e.Cancel = true;
            base.OnClosing(e);
        }
    }
}