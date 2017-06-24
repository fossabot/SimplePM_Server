﻿/*
 * Copyright (C) 2017, Kadirov Yurij.
 * All rights are reserved.
 * Licensed under CC BY-NC-SA 4.0 license.
 * 
 * @Author: Kadirov Yurij
 * @Website: https://sirkadirov.com/
 * @Email: admin@sirkadirov.com
 * @Repo: https://github.com/SirkadirovTeam/SimplePM_Server
 */

//Основа
using System;
//Работа с файловой системой
using System.IO;
//Подключаем коллекции
using System.Collections.Generic;
//Работа с процессами
using System.Diagnostics;
//Работа с потоками
using System.Threading;
using System.Threading.Tasks;
//Безопасность
using System.Web;
//Подключение к MySQL серверу
using MySql.Data.MySqlClient;
//Парсер конфигурационного файла
using IniParser.Model;
//Журнал событий
using NLog;

namespace SimplePM_Server
{
    class SimplePM_Tester
    {
        ///////////////////////////////////////////////////
        // РАЗДЕЛ ОБЪЯВЛЕНИЯ ГЛОБАЛЬНЫХ ПЕРЕМЕННЫХ
        ///////////////////////////////////////////////////

        //Объявляем переменную указателя на менеджер журнала собылий
        //и присваиваем ей указатель на журнал событий текущего класса
        private static Logger logger = LogManager.GetCurrentClassLogger();

        private MySqlConnection connection; // дескриптор соединения с БД
        private Dictionary<string, string> submissionInfo; // информация о запросе на проверку
        private string exeFileUrl, customTestInput; // какие-то параметры
        private ulong problemId, submissionId, userId; // ещё какие-то параметы
        private float problemDifficulty; // сложность задачи
        private IniData sConfig; // дескриптор конфигурационного файла сервера

        ///////////////////////////////////////////////////
        // КОНСТРУКТОР КЛАССА ТЕСТИРОВАНИЯ
        // ПОЛЬЗОВАТЕЛЬСКИХ РЕШЕНИЙ ЗАДАЧ
        ///////////////////////////////////////////////////

        public SimplePM_Tester(ref MySqlConnection connection, ref string exeFileUrl, ref Dictionary<string, string> submissionInfo, ref IniData sConfig)
        {
            //Database connection
            this.connection = connection;
            //Excutable file url
            this.exeFileUrl = exeFileUrl;
            //Submission information
            this.submissionInfo = submissionInfo;
            //Problem ID
            problemId = ulong.Parse(submissionInfo["problemId"]);
            //Submission ID
            submissionId = ulong.Parse(submissionInfo["submissionId"]);
            //Problem difficulty
            problemDifficulty = float.Parse(submissionInfo["difficulty"]);
            //User ID
            userId = ulong.Parse(submissionInfo["userId"]);
            //Configuration file reader pointer
            this.sConfig = sConfig;
            //Custom test checker
            if (submissionInfo.ContainsKey("customTest"))
                customTestInput = submissionInfo["customTest"];
            else
                customTestInput = null;
        }

        #region ИСПОЛЬЗУЕМЫЕ ФУНКЦИИ

        ///////////////////////////////////////////////////
        // ФУНКЦИЯ НОРМАЛИЗАЦИИ ДАННЫХ ВЫХОДНЫХ ПОТОКОВ
        // ПОЛЬЗОВАТЕЛЬСКИХ И АВТОРСКИХ РЕШЕНИЙ ЗАДАЧ
        ///////////////////////////////////////////////////
        private string getNormalizedOutputText(StreamReader outputReader)
        {
            //Создаём переменную, которая будет содержать весь выходной поток
            //авторского решения поставленной задачи
            string _output = "";
            //Создаём временную переменную текущей строки вывода
            string curLine = "";

            while (!outputReader.EndOfStream)
            {
                //Получаем содержимое текущей строки
                curLine = outputReader.ReadLine();

                //Убираем переводы на новую строку
                curLine = curLine.Replace("\n", "");
                //Убираем все начальные и конечные пробелы
                curLine = curLine.Trim(' ');

                if (curLine.Length > 0)
                {
                    //Добавляем перевод на новую строку (если, конечно, это не первая строка)
                    if (_output.Length > 0)
                        curLine = "\n" + curLine;
                }

                //Дозаписываем данные в переменную выходного потока приложения
                _output += curLine;
            }

            return _output;
        }

        ///////////////////////////////////////////////////
        // ФУНКЦИЯ КОНФИГУРАЦИИ ЗАПУСКА ПРИЛОЖЕНИЙ В
        // ЗАВИСИМОСТИ ОТ ИХ ТИПА ИСПОЛНЕНИЯ
        ///////////////////////////////////////////////////
        private void SetExecInfoByFileExt(ref ProcessStartInfo startInfo, string filePath = null)
        {
            if (filePath == null)
                filePath = exeFileUrl;

            switch (Path.GetExtension(filePath))
            {
                //Lua
                case ".lua":
                    startInfo.FileName = sConfig["Compilers"]["lua_location"];
                    startInfo.Arguments = filePath;
                    break;
                //Python
                case ".py":
                    startInfo.FileName = sConfig["Compilers"]["python_location"];
                    startInfo.Arguments = filePath;
                    break;
                //PHP
                case ".php":
                    startInfo.FileName = sConfig["Compilers"]["php_location"];
                    startInfo.Arguments = filePath;
                    break;
                //Executable files
                default:
                    startInfo.FileName = filePath;
                    startInfo.Arguments = "";
                    break;
            }
        }

        #endregion

        #region РЕГИОН ДЕБАГГЕРНЫХ ФУНКЦИЙ И МЕТОДОВ

        ///////////////////////////////////////////////////
        // DEBUG-ТЕСТИРОВАНИЕ ПОЛЬЗОВАТЕЛЬСКИХ РЕШЕНИЙ
        ///////////////////////////////////////////////////
        public void DebugTest()
        {
            //Запрос на выборку авторского решения из БД
            string querySelect = $@"
                SELECT 
                    * 
                FROM 
                    `spm_problems_ready` 
                WHERE 
                    `problemId` = '{problemId.ToString()}' 
                ORDER BY 
                    `problemId` ASC 
                LIMIT 
                    1
                ;
            ";

            //Дескрипторы временных таблиц выборки из БД
            MySqlCommand cmdSelect = new MySqlCommand(querySelect, connection);
            MySqlDataReader dataReader = cmdSelect.ExecuteReader();

            //Создаём словарь значений элемента авторского решения
            Dictionary<string, string> authorCodeInfo = new Dictionary<string, string>();
            //Создаём переменную, в которой будем хранить бинарный код запускаемого авторского решения
            byte[] _authorProblemCode = null;

            //Читаем полученные данные
            while (dataReader.Read())
            {
                //Идентификатор задачи
                authorCodeInfo.Add(
                    "problemId",
                    HttpUtility.HtmlDecode(dataReader["problemId"].ToString())
                );
                //Бинарный (исполняемый) код авторского решения
                _authorProblemCode = (byte[])dataReader["execFile"];
            }

            dataReader.Close();

            if (authorCodeInfo.Count > 0)
            {
                ///////////////////////////////////////////////////
                // РАБОТА С ФАЙЛОВОЙ СИСТЕМОЙ
                ///////////////////////////////////////////////////

                //Полный путь к временному (исполняемому) файлу авторского решения программы
                string authorCodePath = sConfig["Program"]["tempPath"] + "authorCode_" + submissionId + ".exe";

                //Создаём файл и перехватываем поток инъекции в файл данных
                BinaryWriter binWriter = new BinaryWriter(new FileStream(authorCodePath, FileMode.Create));

                //Запись бинарного кода программы в файл исполняемого авторского решения
                binWriter.Write(_authorProblemCode);

                //Записы данных в файл
                binWriter.Flush();

                //Закрываем поток
                binWriter.Close();

                ///////////////////////////////////////////////////
                // ВРЕМЯ ИНТЕРЕСНЕНЬКИХ ЗАПРОСОВ К БАЗЕ ДАННЫХ
                ///////////////////////////////////////////////////

                //Запрос на выборку Debug Time Limit из базы данных MySQL
                querySelect = $@"
                    SELECT 
                        `debugTimeLimit` 
                    FROM 
                        `spm_problems` 
                    WHERE 
                        `id` = '{problemId.ToString()}' 
                    ORDER BY 
                        `id` ASC 
                    LIMIT 
                        1
                    ;
                ";
                ulong debugTimeLimit = Convert.ToUInt64(new MySqlCommand(querySelect, connection).ExecuteScalar());

                //То же самое, только для MEMORY LIMIT
                querySelect = $@"
                    SELECT 
                        `debugMemoryLimit` 
                    FROM 
                        `spm_problems` 
                    WHERE 
                        `id` = '{problemId.ToString()}' 
                    ORDER BY 
                        `id` ASC 
                    LIMIT 
                        1
                    ;
                ";
                ulong debugMemoryLimit = Convert.ToUInt64(new MySqlCommand(querySelect, connection).ExecuteScalar());

                #region PROCESS START INFO CONFIGURATION
                //Создаём новую конфигурацию запуска процесса
                ProcessStartInfo startInfo = new ProcessStartInfo()
                {
                    //Перенаправляем потоки
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,

                    //Запрещаем показ приложения на экран компа
                    ErrorDialog = false,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    Verb = "open",
                    WindowStyle = ProcessWindowStyle.Minimized
                };

                #endregion

                ///////////////////////////////////////////////////
                // ОБЪЯВЛЕНИЕ НЕОБХОДИМЫХ ПЕРЕМЕННЫХ
                ///////////////////////////////////////////////////

                string _authorOutput = "", _userOutput = "";
                char _debugTestingResult = '+';
                int _userProblemExitCode = 0;
                
                ///////////////////////////////////////////////////
                // ЗАПУСК ПРОЦЕССА АВТОРСКОГО РЕШЕНИЯ
                ///////////////////////////////////////////////////

                //Объявляем дескриптор процесса
                Process authorProblemProc = new Process();

                //Указываем полный путь к исполняемому файлу
                startInfo.FileName = authorCodePath;

                //Устанавливаем информацию о запускаемом файле
                SetExecInfoByFileExt(ref startInfo, authorCodePath);

                //Указываем интересующую нас конфигурацию тестирования
                authorProblemProc.StartInfo = startInfo;

                try
                {
                    //Запускаем процесс
                    authorProblemProc.Start();

                    //Инъекция входного потока
                    authorProblemProc.StandardInput.WriteLine(customTestInput); //вставка текста
                    authorProblemProc.StandardInput.Flush(); //запись в поток, очистка буфера
                    authorProblemProc.StandardInput.Close(); //закрываем поток
                }
                catch (Exception) { }

                ///////////////////////////////////////////////////
                // КОНТРОЛЬ ИСПОЛЬЗУЕМОЙ ПРОЦЕССОМ ПАМЯТИ
                ///////////////////////////////////////////////////

                new Task(() =>
                {
                    try
                    {
                        while (!authorProblemProc.HasExited)
                        {
                            //Проверка на превышение лимита памяти
                            if ((ulong)authorProblemProc.PeakWorkingSet64 > debugMemoryLimit) //Лимит памяти превышен
                                authorProblemProc.Kill(); //завершаем работу процесса в принудительном порядке
                        }
                    }
                    catch (Exception) { }
                }).Start();

                //Ждём завершения, максимум X миллимекунд
                authorProblemProc.WaitForExit((int)debugTimeLimit);

                //Если процесс не завершил свою работу - убиваем его
                if (!authorProblemProc.HasExited)
                    authorProblemProc.Kill();

                //Получаем обработанный выходной поток авторского решения
                _authorOutput = getNormalizedOutputText(authorProblemProc.StandardOutput);

                ///////////////////////////////////////////////////
                // ЗАПУСК ПРОЦЕССА ПОЛЬЗОВАТЕЛЬСКОГО РЕШЕНИЯ
                ///////////////////////////////////////////////////

                //Объявляем дескриптор процесса
                Process userProblemProc = new Process();

                //Устанавливаем информацию о запускаемом файле
                SetExecInfoByFileExt(ref startInfo);

                //Указываем интересующую нас конфигурацию тестирования
                userProblemProc.StartInfo = startInfo;

                try
                {
                    //Запускаем процесс
                    userProblemProc.Start();

                    //Инъекция входного потока
                    userProblemProc.StandardInput.WriteLine(customTestInput); //вставка текста
                    userProblemProc.StandardInput.Flush(); //запись в поток, очистка буфера
                    userProblemProc.StandardInput.Close(); //закрываем поток
                }
                catch (Exception ex)
                {
                    //Произошла ошибка при выполнении программы
                    //В этом, скорее всего, виноват компилятор.
                    System.Windows.Forms.MessageBox.Show(ex.HResult.ToString());
                    _debugTestingResult = 'C';
                }

                ///////////////////////////////////////////////////
                // КОНТРОЛЬ ИСПОЛЬЗУЕМОЙ ПРОЦЕССОМ ПАМЯТИ
                ///////////////////////////////////////////////////
                new Task(() =>
                {
                    try
                    {
                        while (!userProblemProc.HasExited)
                        {
                            //Проверка на превышение лимита памяти
                            if ((ulong)userProblemProc.PeakWorkingSet64 > debugMemoryLimit)
                            {
                                //Лимит памяти превышен
                                userProblemProc.Kill(); //завершаем работу процесса в принудительном порядке
                                _debugTestingResult = 'M'; //устанавливаем преждевременный результат тестирования
                            }
                        }
                    }
                    catch (Exception) { }
                }).Start();

                ///////////////////////////////////////////////////
                // ПОЛУЧЕНИЕ РЕЗУЛЬТАТОВ ТЕСТИРОВАНИЯ
                ///////////////////////////////////////////////////

                try
                {

                    //Ждём завершения, максимум X миллимекунд
                    userProblemProc.WaitForExit((int)debugTimeLimit);

                    //Если процесс не завершил свою работу - убиваем его
                    if (!userProblemProc.HasExited)
                    {
                        userProblemProc.Kill();
                        _debugTestingResult = 'T';
                        _userOutput = "--- TIME LIMIT ---";
                    }

                    //Получаем обработанный выходной поток пользовательского решения
                    //но только в случае, когда приложение завершило свою работу самопроизвольно
                    if (_userOutput.Length == 0)
                        _userOutput = getNormalizedOutputText(userProblemProc.StandardOutput);

                    //Получаем exitcode пользовательского приложения
                    _userProblemExitCode = userProblemProc.ExitCode;

                    //Пытаемся удалить временный файл авторского решения поставленной задачи
                    try
                    {
                        File.Delete(authorCodePath);
                    }
                    catch (Exception) { }

                    //Устанавливаем результат отладочного тестирования.
                    //В случае преждевременного результата ничего не делаем
                    if (_debugTestingResult == '+')
                    {
                        if (_authorOutput == _userOutput)
                            _debugTestingResult = '+';
                        else
                            _debugTestingResult = '-';
                    }

                }
                catch (Exception) { }

                ///////////////////////////////////////////////////
                // ПОЛУЧЕНИЕ ВЫХОДНОГО ПОТОКА
                // ПОЛЬЗОВАТЕЛЬСКОЙ ПРОГРАММЫ
                ///////////////////////////////////////////////////

                _userOutput = HttpUtility.HtmlEncode(_userOutput);

                ///////////////////////////////////////////////////
                // СОХРАНЯЕМ ИНФОРМАЦИЮ О РЕЗУЛЬТАТЕ ТЕСТИРОВАНИЯ
                // НА СЕРВЕРЕ БАЗ ДАННЫХ MYSQL
                ///////////////////////////////////////////////////

                string queryUpdate = $@"
                    UPDATE 
                        `spm_submissions` 
                    SET 
                        `status` = 'ready', 
                        `errorOutput` = null, 
                        `result` = '{_debugTestingResult}', 
                        `b` = '0', 
                        `output` = '{_userOutput}', 
                        `exitcodes` = '{_userProblemExitCode}' 
                    WHERE 
                        `submissionId` = '{submissionId.ToString()}' 
                    LIMIT 
                        1
                    ;
                ";
                new MySqlCommand(queryUpdate, connection).ExecuteNonQuery();
            }
            else
            {
                ///////////////////////////////////////////////////
                // НАША ХАТА СКРАЮ - НІЧОГО НЕ ЗНАЮ!
                ///////////////////////////////////////////////////

                string queryUpdate = $@"
                    UPDATE 
                        `spm_submissions` 
                    SET 
                        `status` = 'waiting', 
                        `hasError` = false 
                    WHERE 
                        `submissionId` = '{submissionId}' 
                    LIMIT 
                        1
                    ;
                ";
                new MySqlCommand(queryUpdate, connection).ExecuteNonQuery();
            }
        }

        #endregion

        #region РЕГИОН РЕЛИЗНЫХ ФУНКЦИЙ И МЕТОДОВ

        ///////////////////////////////////////////////////
        // RELEASE-ТЕСТИРОВАНИЕ ПОЛЬЗОВАТЕЛЬСКИХ РЕШЕНИЙ
        ///////////////////////////////////////////////////

        public void ReleaseTest()
        {
            ///////////////////////////////////////////////////
            // ВРЕМЯ ИНТЕРЕСНЕНЬКИХ ЗАПРОСОВ К БАЗЕ ДАННЫХ
            ///////////////////////////////////////////////////

            //Запрос на выборку всех тестов данной программы из БД
            string querySelect = $@"
                SELECT 
                    * 
                FROM 
                    `spm_problems_tests`
                WHERE 
                    `problemId` = '{problemId.ToString()}' 
                ORDER BY 
                    `id` ASC
                ;
            ";

            //Дескрипторы временных таблиц выборки из БД
            MySqlCommand cmdSelect = new MySqlCommand(querySelect, connection);
            MySqlDataReader dataReader = cmdSelect.ExecuteReader();

            //Переменная результата выполнения всех тестов
            string _problemTestingResult = "";
            int _problemPassedTests = 0;

            //Объявляем словарь информации о тестах
            Dictionary<int, Dictionary<string, string>> testsInfo = new Dictionary<int, Dictionary<string, string>>();

            //Производим выборку полученных результатов из временной таблицы на сервере MySQL
            int i = 1;
            while (dataReader.Read())
            {
                Dictionary<string, string> tmpDict = new Dictionary<string, string>
                {
                    { "testId", HttpUtility.HtmlDecode(dataReader["id"].ToString()) }, //Идентификатор теста
                    { "input", HttpUtility.HtmlDecode(dataReader["input"].ToString()) }, //Входной поток
                    { "output", HttpUtility.HtmlDecode(dataReader["output"].ToString()) }, //Выходной поток
                    { "timeLimit", HttpUtility.HtmlDecode(dataReader["timeLimit"].ToString()) }, //Лимит по времени
                    { "memoryLimit", HttpUtility.HtmlDecode(dataReader["memoryLimit"].ToString()) } //Лимит по памяти
                };

                //Добавляем в словарь
                testsInfo.Add(i, tmpDict);

                //Увеличиваем индекс текущего теста на единицу
                i++;
            }

            //Завершаем чтение потока
            dataReader.Close();

            ///////////////////////////////////////////////////

            #region PROCESS START INFO CONFIGURATION
            //Создаём новую конфигурацию запуска процесса
            ProcessStartInfo startInfo = new ProcessStartInfo()
            {
                //Перенаправляем потоки
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,

                //Запрещаем исполнение приложения в графическом режиме
                ErrorDialog = false,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Minimized,
            };

            //Устанавливаем информацию о запускаемом файле
            SetExecInfoByFileExt(ref startInfo);

            #endregion

            //Объявление необходимых переменных для тестирования
            //пользовательской программы
            ulong testId; //идентификатор теста
            string input, output; //входной и выходной потоки теста
            int timeLimit; //лимит времени теста
            long memoryLimit; //лимит памяти теста
            string standartErrorOutputText = null; //переменная стандартного потока ошибок
            bool preResultGiven = false; //служебная переменная для определения предопределённого результата
            string _exitcodes = "|"; //переменная кодов выхода

            for (i = 1; i <= testsInfo.Count; i++)
            {
                //Присвоение переменных данных теста для быстрого доступа
                testId = ulong.Parse(testsInfo[i]["testId"]);
                input = testsInfo[i]["input"].ToString();
                output = testsInfo[i]["output"].ToString().Replace("\r\n", "\n");
                timeLimit = int.Parse(testsInfo[i]["timeLimit"]);
                memoryLimit = long.Parse(testsInfo[i]["memoryLimit"]);

                //Объявляем дескриптор процесса
                Process problemProc = new Process()
                {
                    //Указываем интересующую нас конфигурацию тестирования
                    StartInfo = startInfo
                };

                ///////////////////////////////////////////////////
                // ЗАПУСК ПРОЦЕССА И ПЕРЕХВАТ I/O ПОТОКОВ
                ///////////////////////////////////////////////////

                try
                {
                    //Запускаем процесс
                    problemProc.Start();

                    //Инъекция входного потока
                    problemProc.StandardInput.WriteLine(input); //добавляем текст во входной поток
                    problemProc.StandardInput.Flush(); //производим запись во входной поток и последующую очистку буфера
                    problemProc.StandardInput.Close(); //закрываем запись входного потока
                }
                catch (Exception)
                {
                    //Произошла ошибка при выполнении программы.
                    //Виноват, как всегда, компилятор!

                    _problemTestingResult += "C";
                    preResultGiven = true;
                }

                ///////////////////////////////////////////////////
                // КОНТРОЛЬ ИСПОЛЬЗУЕМОЙ ПРОЦЕССОМ ПАМЯТИ
                ///////////////////////////////////////////////////
                new Task(() =>
                {
                    //Для обеспечения безопасности
                    try
                    {
                        while (!problemProc.HasExited)
                        {
                            //Проверка на превышение лимита памяти
                            if (problemProc.PeakWorkingSet64 > memoryLimit)
                            {
                                //Лимит памяти превышен
                                problemProc.Kill(); //завершаем работу процесса в принудительном порядке
                                _problemTestingResult += "M"; //устанавливаем преждевременный результат тестирования
                                preResultGiven = true; //указываем, что выдали преждевременный результат тестирования
                            }
                        }
                    }
                    catch (Exception) { }

                }).Start();

                ///////////////////////////////////////////////////
                // ОЖИДАНИЕ ЗАВЕРШЕНИЯ ПОЛЬЗОВАТЕЛЬСКОЙ ПРОГРАММЫ
                ///////////////////////////////////////////////////

                try
                {
                    //Ждём завершения, максимум X миллимекунд
                    problemProc.WaitForExit(timeLimit);
                }
                catch (Exception) { }

                //Проверка на досрочный результат проверки
                if (!preResultGiven)
                {

                    if (!problemProc.HasExited)
                    {
                        //Процесс не завершил свою работу
                        //Исключение: времени не хватило!
                        problemProc.Kill();
                        //Методом научного тыка было выявлено, что необходимо 10 мс чтобы программа
                        //корректно завершила свою работу
                        Thread.Sleep(10);
                        _problemTestingResult += 'T';
                    }
                    else if (problemProc.HasExited)
                    {
                        //Проверка на "вшивость"
                        string currentErrors = problemProc.StandardError.ReadToEnd();
                        problemProc.StandardError.Close();

                        //Добавляем ошибки текущего теста в список всех ошибок
                        if (currentErrors.Length > 0)
                            standartErrorOutputText += currentErrors;

                        if (currentErrors.Length > 0)
                        {
                            //Ошибка при тесте!
                            _problemTestingResult += 'E';
                        }
                        else
                        {
                            //Ошибок при тесте не выявлено, но вы держитесь!

                            //Читаем выходной поток приложения
                            string pOut = getNormalizedOutputText(problemProc.StandardOutput);
                            //Добавляем результат
                            if (output == pOut)
                            {
                                _problemTestingResult += '+';
                                _problemPassedTests++;
                            }
                            else if (problemProc.ExitCode != 0)
                                _problemTestingResult += 'R';
                            else
                                _problemTestingResult += '-';
                        }

                    }

                    //Добавляем конечную палку для правильности
                    //последующих парсингов
                    _exitcodes += problemProc.ExitCode + "|";

                }

                ///////////////////////////////////////////////////
                // НА ВСЯКИЙ СЛУЧАЙ ПЫТАЕМСЯ ЗАВЕРШИТЬ РАБОТУ
                // ПРОЦЕССА ПОЛЬЗОВАТЕЛЬСКОЙ ПРОГРАММЫ
                ///////////////////////////////////////////////////
                try
                {
                    problemProc.Kill();
                }
                catch (Exception) {  }
            }

            //Объявляем переменную численного результата тестирования
            float _bResult = 0;

            try
            {
                //Тестов нет, но вы держитесь!
                if (_problemTestingResult.Length <= 0)
                {
                    _problemTestingResult = "+";
                    _problemPassedTests = 1;
                }

                //Вычисляем полученный балл за решение задачи
                //на основе количества пройденных тестов

                int testsCount = (testsInfo.Count == 0 ? 1 : testsInfo.Count);

                _bResult = (_problemPassedTests / testsCount) * problemDifficulty;
            }
            catch (Exception) { _bResult = problemDifficulty; _problemTestingResult = "+"; }

            //ОТПРАВКА РЕЗУЛЬТАТОВ ТЕСТИРОВАНИЯ НА СЕРВЕР БД
            //В ТАБЛИЦУ РЕЗУЛЬТАТОВ (`spm_submissions`)
            string queryUpdate = @"
                UPDATE 
                    `spm_submissions` 
                SET 
                    `status` = 'ready', 
                    `errorOutput` = @errorOutput, 
                    `result` = @result, 
                    `exitcodes` = @exitcodes, 
                    `b` = @b 
                WHERE 
                    `submissionId` = @submId 
                LIMIT 
                    1
                ;
            ";
            //Создаём запрос к базе данных MySql
            MySqlCommand cmdUpd = new MySqlCommand(queryUpdate, connection);
            //Безопасно добавляем отправляемые данные
            cmdUpd.Parameters.AddWithValue("@errorOutput", standartErrorOutputText);
            cmdUpd.Parameters.AddWithValue("@result", _problemTestingResult);
            cmdUpd.Parameters.AddWithValue("@exitcodes", _exitcodes);
            cmdUpd.Parameters.AddWithValue("@b", _bResult);
            cmdUpd.Parameters.AddWithValue("@submId", submissionId.ToString());
            //Выполняем запрос
            cmdUpd.ExecuteNonQuery();

            #region Установка авторского решения

            //Составляем SQL запрос на выборку из записи запроса на проверку 
            querySelect = $@"
                SELECT 
                    `setAsAuthorSolution` 
                FROM 
                    `spm_submissions` 
                WHERE 
                    `submissionId` = '{submissionId}' 
                LIMIT 
                    1
                ;
            ";
            //Получаем результат выполнения запроса
            bool setAsAuthorSolution = Convert.ToBoolean(new MySqlCommand(querySelect, connection).ExecuteScalar());

            //Проверка на запрос установить ткущее решение как авторское
            if (setAsAuthorSolution)
            {
                //Составляем запрос на выборку из БД решения задачи и язык её решения по текущей попытке
                querySelect = $@"
                    SELECT 
                        `problemCode`, 
                        `codeLang` 
                    FROM 
                        `spm_submissions` 
                    WHERE 
                        `submissionId` = '{submissionId}' 
                    LIMIT 
                        1
                    ;
                ";
                //Получаем результат выполнения запроса
                dataReader = new MySqlCommand(querySelect, connection).ExecuteReader();
                if (dataReader.Read())
                {
                    try
                    {
                        //Исходный код авторского решения
                        string problemCode = dataReader["problemCode"].ToString();
                        //Язык написания авторского решения
                        string problemLang = dataReader["codeLang"].ToString();

                        //ПОЛУЧЕНИЕ БИНАРНОГО КОДА АВТОРСКОГО РЕШЕНИЯ

                        //Создаём экземпляр бинарного смотрителя
                        BinaryReader binReader = new BinaryReader(new FileStream(exeFileUrl, FileMode.Open));
                        //Читаем весь бинарный код до конца файла в строку, полученную строку
                        //форматируем, чтобы не допустить "ломание" запроса к базе данных MySQL
                        byte[] problemBinCode = binReader.ReadBytes((int)binReader.BaseStream.Length);
                        //Освобождаем ресурсы, используемые потоком
                        binReader.Dispose();
                        //Закрываем поток
                        binReader.Close();

                        //Закрываем соединение с базой данных (временное).
                        //Временные таблицы, расположенные на MySQL сервере, при этом удаляются.
                        dataReader.Close();

                        //Формируем запрос на добавление/обновление авторского
                        //решения для данной задачи.

                        queryUpdate = $@"
                            INSERT INTO 
                                `spm_problems_ready` 
                            SET 
                                `problemId` = '{problemId}', 
                                `execFile` = @problemBinCode, 
                                `codeLang` = '{problemLang}', 
                                `code` = @problemCode 
                            ON DUPLICATE KEY UPDATE 
                                `execFile` = @problemBinCode,
                                `codeLang` = '{problemLang}', 
                                `code` = @problemCode
                            ;
                        ";

                        MySqlCommand insertCmd = new MySqlCommand(queryUpdate, connection);

                        insertCmd.Parameters.AddWithValue("@problemBinCode", problemBinCode);
                        insertCmd.Parameters.AddWithValue("@problemCode", problemCode);

                        //Выполняем запрос к базе данных на добавление/обновление
                        //авторского решения для данной задачи.
                        insertCmd.ExecuteNonQuery();
                    }
                    catch (Exception) { }
                }
                else
                    dataReader.Close();
            }

            #endregion

            //Обновляем количество баллов и рейтинг пользователя
            //для этого вызываем пользовательские процедуры mysql,
            //созданные как раз для этих нужд
            //P.S. но делаем это в том случае, если попытка была
            //произведена не во время олимпиады и не во время урока
            if (ulong.Parse(submissionInfo["classworkId"]) == 0 && ulong.Parse(submissionInfo["olympId"]) == 0)
                new MySqlCommand($"CALL updateBCount({userId}); CALL updateRating({userId})", connection).ExecuteNonQuery();
        }

        #endregion
    }
}
