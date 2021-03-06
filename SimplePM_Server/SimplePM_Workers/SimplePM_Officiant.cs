﻿/*
 * Copyright (C) 2017, Kadirov Yurij.
 * All rights are reserved.
 * Licensed under Apache License 2.0 with additional restrictions.
 * 
 * @Author: Kadirov Yurij
 * @Website: https://sirkadirov.com/
 * @Email: admin@sirkadirov.com
 * @Repo: https://github.com/SirkadirovTeam/SimplePM_Server
 */

using NLog;
using System;
using System.IO;
using CompilerBase;
using MySql.Data.MySqlClient;
using System.Collections.Generic;
using System.Text;
using SimplePM_Server.SimplePM_Tester;

namespace SimplePM_Server
{

    /*
     * Класс официанта, который занимается
     * обработкой пользовательских запросов
     * на тестирование решений задач по
     * программированию.
     */

    internal class SimplePM_Officiant
    {

        /*
         * Объявляем переменную указателя
         * на менеджер  журнала собылий и
         * присваиваем  ей  указатель  на
         * журнал событий текущего класса
         */
        private static readonly Logger logger = LogManager.GetLogger("SimplePM_Officiant");

        // Дескриптор соединения с БД
        private MySqlConnection _connection;
        // Содержит информацию о запросе на тестирование
        private SubmissionInfo.SubmissionInfo _submissionInfo;
        // Содержит конфигурацию сервера
        private dynamic _serverConfiguration;
        // Конфигурация модулей компиляции
        private dynamic _compilerConfigurations;
        // Список, содержащий активные модули компиляции
        private List<ICompilerPlugin> _compilerPlugins;
        
        public SimplePM_Officiant(
            MySqlConnection connection,
            ref dynamic _serverConfiguration,
            ref dynamic _compilerConfigurations,
            ref List<ICompilerPlugin> _compilerPlugins,
            SubmissionInfo.SubmissionInfo submissionInfo
        )
        {
            
            _connection = connection;

            this._serverConfiguration = _serverConfiguration;
            this._compilerConfigurations = _compilerConfigurations;

            this._compilerPlugins = _compilerPlugins;

            _submissionInfo = submissionInfo;

        }

        /*
         * Метод  генерирует  случайный  путь  к  файлу
         * исходного  кода   пользовательского  решения
         * поставленной задачи. Случайным  данный  путь
         * должен  быть для обеспечения  дополнительной
         * безопасности при запуске сторонних программ.
         */
        
        private string RandomGenSourceFileLocation(string submissionId, string fileExt)
        {

            // Генерируем имя директории
            var directoryName = (string)_serverConfiguration.path.temp + 
                                @"\" + Guid.NewGuid() + 
                                submissionId + @"\";

            // Создаём все необходимые каталоги
            Directory.CreateDirectory(directoryName);

            // Возвращаем результат работы функции
            return directoryName + "s" + submissionId + fileExt;

        }

        /*
         * Метод сопровождает пользовательский
         * запрос на тестирование данного решения
         * поставленной задачи на всех стадиях
         * тестирования, и занимается вызовом
         * всех необходимых для осуществления
         * тестирования пользовательского решения
         * методов.
         */

        public void ServeSubmission()
        {
            
            /*
             * Записываем в лог-файл информацию о том,
             * что начата обработка данного запроса на
             * тестирование.
             */
            logger.Trace("Serving submission #" + _submissionInfo.SubmissionId + " started!");

            /*
             * Определяем соответствующую данному запросу
             * на тестирование конфигурацию модуля компиляции
             */

            var compilerConfiguration = SimplePM_Compiler.GetCompilerConfig(
                ref _compilerConfigurations,
                _submissionInfo.CodeLang
            );

            var compilerPlugin = SimplePM_Compiler.FindCompilerPlugin(
                ref _compilerPlugins,
                (string)compilerConfiguration.module_name
            );

            /*
             * Определяем расширение файла
             * исходного кода пользовательского
             * решения поставленной задачи.
             */

            var fileExt = "." + compilerConfiguration.source_ext;
            
            /*
             * Случайным образом генерируем путь
             * к файлу исходного кода пользовательского
             * решения поставленной задачи.
             */

            var fileLocation = RandomGenSourceFileLocation(
                _submissionInfo.SubmissionId.ToString(),
                fileExt
            );
            
            /*
             * Записываем в него исходный код,
             * очищаем буфер и закрываем поток
             * записи в данный файл.
             * При этом, осуществляем побайтовую
             * запись в файл, дабы не повредить его.
             */

            File.WriteAllBytes(
                fileLocation,
                _submissionInfo.ProblemCode
            );

            /*
             * Устанавливаем аттрибуты данного файла
             * таким образом, дабы исключить возможность
             * индексирования его содержимого и остальные
             * не приятные для нас ситуации, которые могут
             * привести к непредвиденным последствиям.
             */

            File.SetAttributes(
                fileLocation,
                FileAttributes.NotContentIndexed
            );

            /*
             * Объявляем и нициализируем переменную,
             * в которой будет храниться ссылка на
             * объект, методы которого осуществляют
             * все этапы компиляции пользовательского
             * решения задачи.
             */

            var compiler = new SimplePM_Compiler(
                ref compilerConfiguration,
                ref compilerPlugin,
                _submissionInfo.SubmissionId.ToString(),
                fileLocation
            );
            
            /*
             * Вызываем функцию  запуска  компилятора для
             * данного языка программирования.
             * Функция возвращает информацию о результате
             * компиляции пользовательской программы.
             */

            var cResult = compiler.ChooseCompilerAndRun();
            
            /*
             * Запускаем тестирование пользовательской
             * программы  по  указанному  его  типу  и
             * парметрам,   после  чего  получаем  его
             * результат.
             */

            var testingResult = RunTesting(
                cResult,
                ref compilerConfiguration,
                ref compilerPlugin
            );
            
            /*
             * Отправляем результат тестирования
             * пользовательского решения постав-
             * ленной  задачи  на  сервер БД для
             * последующей обработки.
             */

            SendTestingResult(testingResult, cResult);

            /*
             * Вызываем метод,  который несёт
             * ответственность  за   удаление
             * всех временных файлов запросов
             * на  тестирование,  а  также за
             * вызов сборщика мусора.
             */
            
            ClearCache(fileLocation);

            /*
             * Записываем в лог-файл  информацию о том,
             * что    тестирование    пользовательского
             * решения завершено, но нам всё равно как.
             */
            
            logger.Trace(
                "#" +
                _submissionInfo.SubmissionId +
                ": Submission testing completed!"
            );

        }

        private ProgramTestingResult RunTesting(
            CompilerResult cResult,
            ref dynamic compilerConfiguration,
            ref ICompilerPlugin compilerPlugin
        )
        {

            /*
             * Записываем   в   лог-файл  информацию  о  том,
             * что в данный  момент  производим  тестирование
             * пользовательского решения поставленной задачи.
             */

            logger.Trace(
                "#" + _submissionInfo.SubmissionId +
                ": Running solution testing subsystem (" +
                _submissionInfo.TestType +
                " mode)..."
            );

            /*
             * Объявляем временную переменную,
             * которая будет хранить результат
             * выполнения тестирования пользо-
             * вательской программы.
             */

            ProgramTestingResult tmpResult = null;

            /*
             * Объявляем  объект на базе класса
             * тестировщика, с помощью которого
             * в скором времени будем выполнять
             * тестировани пользовательских ре-
             * шений задач по программированию.
             */

            var tester = new SimplePM_Tester.SimplePM_Tester(
                ref _connection,
                ref _serverConfiguration,
                ref _compilerConfigurations,
                ref _compilerPlugins,
                cResult.ExeFullname,
                ref _submissionInfo
            );

            /*
             * Выполняем тестирование в блоке обработки
             * исключительных систуаций,  и,  в  случае
             * возникновения такой  ситуации записываем
             * данные о ней в лог-файл  и  искусственно
             * создаём     результаты      тестирования
             * пользовательского решения задачи.
             */

            try
            {

                /*
                 * В зависимости от выбранного пользователем
                 * типа тестирования выполняем специфические
                 * операции по отношению к решению задачи.
                 */

                switch (_submissionInfo.TestType)
                {

                    /* Проверка синтаксиса */
                    case "syntax":

                        /*
                         * Вызываем   метод,   который   создаёт
                         * иллюзию  проверки   пользовательского
                         * решения    поставленной    задачи   и
                         * возвращает результаты своей "работы".
                         */

                        tmpResult = tester.Syntax();

                        break;

                    /* Debug-тестирование */
                    case "debug":

                        /*
                         * Вызываем метод Debug-тестирования
                         * пользовательского решения постав-
                         * ленной задачи по программированию
                         */

                        tmpResult = tester.Debug();

                        break;

                    /* Release-тестирование */
                    case "release":

                        /*
                         * Вызываем метод Release-тестирования
                         * пользовательского  решения  постав-
                         * ленной задачи  по  программированию
                         */

                        tmpResult = tester.Release(
                            ref compilerConfiguration,
                            ref compilerPlugin
                        );

                        break;

                }

            }
            catch (Exception ex)
            {

                // Записываем информацию об исключении в лог
                logger.Error(ex);

                /*
                 * Создаём псевдорезультат тестирования,
                 * который будет содержать информацию о
                 * возникшем во время тестирования поль-
                 * зовательского решения задачи исключе-
                 * нии.
                 */

                tmpResult = new ProgramTestingResult(1)
                {

                    /*
                     * Создаём превдотест и добавляем его
                     * в массив результатов тестирования.
                     */
                    TestingResults =
                    {

                        [0] = new TestResult
                        {
                            
                            // За код выхода принимаем номер исклбчения
                            ExitCode = ex.HResult,

                            // За выходной поток ошибок принимаем исключение
                            ErrorOutput = ex.ToString(),

                            // Заполняем выходные данные информацией (not null)
                            Output = Encoding.UTF8.GetBytes("An exception occured during testing!"),

                            // Устанавливаем результат тестирования
                            Result = TestResult.ErrorOutputNotNullResult,

                            /*
                             * Указываем, что память и процессорное
                             * время не были использованы.
                             */

                            UsedMemory = 0,
                            UsedProcessorTime = 0

                        }

                    }

                };

            }

            /*
             * Возвращаем результат выполнения
             * тестирования  пользовательского
             * решения данной задачи.
             */

            return tmpResult;

        }

        private void SendTestingResult(ProgramTestingResult ptResult, CompilerResult cResult)
        {

            /*
             * Указываем в лог-файле о скором
             * завершении  обработки  данного
             * запроса на тестирование.
             */

            logger.Trace(
                "#" +
                _submissionInfo.SubmissionId +
                ": Result is being sent to MySQL server..."
            );
            
            /*
             * Создаём команду для MySQL сервера
             * на     основе     сформированного
             * запроса к базе данных.
             */

            var updateSqlCommand = new MySqlCommand(
                Properties.Resources.submission_result_query,
                _connection
            );

            /*
             * Указываем параметры выше сформированного
             * запроса к базе данных.
             */
            
            updateSqlCommand.Parameters.AddWithValue(
                "@param_submissionId",
                _submissionInfo.SubmissionId
            ); // Идентификатор запроса
            
            updateSqlCommand.Parameters.AddWithValue(
                "@param_hasError",
                Convert.ToInt32(cResult.HasErrors)
            ); // Сигнал об ошибке при компиляции

            updateSqlCommand.Parameters.AddWithValue(
                "@param_compiler_text",
                cResult.CompilerMessage
            ); // Вывод компилятора
            
            updateSqlCommand.Parameters.AddWithValue(
                "@param_errorOutput",
                Encoding.UTF8.GetBytes(ptResult.GetErrorOutputAsLine())
            ); // Вывод ошибок решения

            updateSqlCommand.Parameters.AddWithValue(
                "@param_output",
                ptResult.TestingResults[ptResult.TestingResults.Length - 1].Output
            ); // Вывод решения

            updateSqlCommand.Parameters.AddWithValue(
                "@param_exitcodes",
                ptResult.GetExitCodesAsLine('|')
            ); // Коды выхода решения

            updateSqlCommand.Parameters.AddWithValue(
                "@param_usedProcTime",
                ptResult.GetUsedProcessorTimeAsLine('|')
            ); // Использованное процессорное время решения

            updateSqlCommand.Parameters.AddWithValue(
                "@param_usedMemory",
                ptResult.GetUsedMemoryAsLine('|')
            ); // Использованная память решения
            
            updateSqlCommand.Parameters.AddWithValue(
                "@param_result",
                ptResult.GetResultAsLine('|')
            ); // Потестовые результаты решения
            
            updateSqlCommand.Parameters.AddWithValue(
                "@param_rating",
                0
            ); // Полученный рейтинг за решение

            /*
             * Выполняем запрос к базе данных
             */
            
            updateSqlCommand.ExecuteNonQuery();

        }

        /*
         * Метод занимается  очисткой  временных
         * файлов,  которрые  были  созданы  при
         * обработке  пользовательского  запроса
         * на тестирование решения данной задачи
         * по алгоритмическому  или  спортивному
         * программированию.
         */
        private static void ClearCache(string fileLocation)
        {

            /*
             * Очищаем папку экзешников от мусора.
             * В случае ошибки ничего не делаем.
             */

            try
            {

                // Удаляем каталог временных файлов
                Directory.Delete(
                    new FileInfo(fileLocation).DirectoryName,
                    true
                );

                // Вызываем сборщик мусора оптимизированным методом
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized);

            }
            catch (Exception ex)
            {

                // Записываем исключение в лог
                logger.Warn("Cache clearing failed: " + ex);

            }

        }
        
    }
}
