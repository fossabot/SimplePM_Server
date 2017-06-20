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
//Подключаем коллекции
using System.Collections.Generic;
//Подключение к БД
using MySql.Data.MySqlClient;
//Конфигурационный файл
using IniParser.Model;
//Работа с файлами
using System.IO;
//Журнал событий
using NLog;

namespace SimplePM_Server
{
    class SimplePM_Officiant
    {
        //Объявляем переменную указателя на менеджер журнала собылий
        //и присваиваем ей указатель на журнал событий текущего класса
        private static Logger logger = LogManager.GetCurrentClassLogger();

        //Объявляем необходимые переменные
        private MySqlConnection connection; //дескриптор соединения с БД
        private Dictionary<string, string> submissionInfo; //словарь информации о запросе
        private IniData sConfig; //дескриптор конфигурационного файла

        public SimplePM_Officiant(MySqlConnection connection, IniData sConfig, Dictionary<string, string> submissionInfo)
        {
            this.connection = connection;
            this.sConfig = sConfig;
            this.submissionInfo = submissionInfo;
        }
        /// <summary>
        /// Процедура, которая отвечает за обработку пользовательского запроса
        /// </summary>
        public void serveSubmission()
        {
            //Определяем язык написания пользовательской программы
            SimplePM_Submission.SubmissionLanguage codeLang = SimplePM_Submission.getCodeLanguageByName(submissionInfo["codeLang"]);

            //Определяем расширение файла
            string fileExt = "." + SimplePM_Submission.getExtByLang(codeLang);
            //Определяем полный путь к файлу
            string fileLocation = sConfig["Program"]["tempPath"] + submissionInfo["submissionId"] + fileExt;

            //Создаём файл исходного кода
            StreamWriter codeWriter = File.CreateText(fileLocation);

            //Устанавливаем его аттрибуты
            File.SetAttributes(fileLocation, FileAttributes.Temporary | FileAttributes.NotContentIndexed);
            
            //Записываем в него исходный код, очищаем буфер и закрываем поток записи
            codeWriter.WriteLine(submissionInfo["problemCode"]);
            codeWriter.Flush();
            codeWriter.Close();

            //Объявляем экземпляр класса компиляции
            SimplePM_Compiler compiler = new SimplePM_Compiler(ref sConfig, ulong.Parse(submissionInfo["submissionId"]), fileExt);
            //Объявляем переменную результата компиляции
            SimplePM_Compiler.CompilerResult cResult;

            //Запускаем определённый компилятор в зависимости от языка решения задачи
            switch (codeLang)
            {
                case SimplePM_Submission.SubmissionLanguage.freepascal:
                    //Запускаем компилятор
                    cResult = compiler.startFreepascalCompiler();
                    break;
                case SimplePM_Submission.SubmissionLanguage.lua:
                    //LUA файлам не требуется компиляция
                    //но для обратной совместимости функцию вкатать нужно
                    cResult = compiler.startLuaCompiler();
                    break;
                case SimplePM_Submission.SubmissionLanguage.csharp:
                    //Запускаем компилятор
                    cResult = compiler.startCSharpCompiler();
                    break;
                case SimplePM_Submission.SubmissionLanguage.python:
                    //Запускаем компилятор
                    cResult = compiler.startCSharpCompiler();
                    break;
                default:
                    cResult = new SimplePM_Compiler.CompilerResult();
                    cResult.hasErrors = true;
                    cResult.compilerMessage = "Language not supported!";
                    break;
            }
            

            //Записываем в базу данных сообщение компилятора
            string queryUpdate = $@"
                UPDATE 
                    `spm_submissions` 
                SET 
                    `compiler_text` = '{cResult.compilerMessage}' 
                WHERE 
                    `submissionId` = '{submissionInfo["submissionId"]}' 
                LIMIT 
                    1
                ;
            ";
            new MySqlCommand(queryUpdate, connection).ExecuteNonQuery();

            //Проверяем на наличие ошибок компиляции
            if (cResult.hasErrors)
            {
                //Ошибка компиляции, записываем это в БД
                queryUpdate = $@"
                    UPDATE 
                        `spm_submissions` 
                    SET 
                        `status` = 'ready', 
                        `hasError` = true 
                    WHERE 
                        `submissionId` = '{submissionInfo["submissionId"]}' 
                    LIMIT 
                        1
                    ;
                ";
                new MySqlCommand(queryUpdate, connection).ExecuteNonQuery();
            }
            else
            {
                try
                {
                    //Выполняем различные действия в зависимости от теста
                    switch (submissionInfo["testType"])
                    {
                        //Проверка синтаксиса
                        case "syntax":

                            queryUpdate = $@"
                                UPDATE 
                                    `spm_submissions` 
                                SET 
                                    `status` = 'ready' 
                                WHERE 
                                    `submissionId` = '{submissionInfo["submissionId"].ToString()}' 
                                LIMIT 
                                    1
                                ;
                            ";
                            new MySqlCommand(queryUpdate, connection).ExecuteNonQuery();

                            break;
                        //Отладка программы по пользовательскому тесту
                        case "debug":
                            //Запускаем тестирование программы
                            new SimplePM_Tester(
                                ref connection, //дескриптор соединения с БД
                                ref cResult.exe_fullname, //полный путь к исполняемому файлу
                                ref submissionInfo, //информация о запросе на тестирование
                                ref sConfig //дескриптор конфигурационного файла сервера
                            ).DebugTest();
                            break;
                        //Отправка решения задачи
                        case "release":

                            //Запускаем тестирование программы
                            new SimplePM_Tester(
                                ref connection, //дескриптор соединения с БД
                                ref cResult.exe_fullname, //полный путь к исполняемому файлу
                                ref submissionInfo, //информация о запросе на тестирование
                                ref sConfig
                            ).ReleaseTest();

                            break;
                    }
                }
                catch (Exception)
                {
                    //Вызываем сборщика мусора
                    clearCache(cResult.exe_fullname, fileLocation);

                    //Делаем так, чтобы несчастливую отправку обрабатывал
                    //кто-то другой, но только не мы (а может и мы, но позже)
                    queryUpdate = $@"
                        UPDATE 
                            `spm_submissions` 
                        SET 
                            `status` = 'waiting' 
                        WHERE 
                            `submissionId` = '{submissionInfo["submissionId"].ToString()}' 
                        LIMIT 
                            1
                        ;
                    ";
                    new MySqlCommand(queryUpdate, connection).ExecuteNonQuery();

                    //Выходим
                    return;
                }
                
            }

            //Вызываем сборщика мусора
            clearCache(cResult.exe_fullname, fileLocation);
        }

        private void clearCache(string exe_fullname, string fileLocation)
        {
            //Очищаем папку экзешников от мусора
            try
            {
                File.Delete(exe_fullname);
                File.Delete(fileLocation);
                GC.Collect(2, GCCollectionMode.Optimized);
            }
            catch (Exception) { }
        }
    }
}
