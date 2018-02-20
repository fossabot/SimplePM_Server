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

using System.Diagnostics;
using CompilerBase;

namespace CompilerPlugin
{
    
    public class Compiler : ICompilerPlugin
    {

        public string PluginName => "TypicalCompiler";
        public string AuthorName => "Kadirov Yurij";
        public string SupportUri => "https://spm.sirkadirov.com/";

        public CompilerResult StartCompiler(ref dynamic languageConfiguration, string submissionId, string fileLocation)
        {

            // Инициализируем объект CompilerRefs
            var cRefs = new CompilerRefs();

            //Будущее местонахождение исполняемого файла
            var exeLocation = cRefs.GenerateExeFileLocation(
                fileLocation,
                submissionId
            );

            //Запуск компилятора с заранее определёнными аргументами
            var result = cRefs.RunCompiler(
                languageConfiguration.compiler_path,
                languageConfiguration.compiler_arguments
            );

            //Передаём полный путь к исполняемому файлу
            result.ExeFullname = exeLocation;

            //Возвращаем результат компиляции
            return cRefs.ReturnCompilerResult(result);

        }
        
        public bool SetRunningMethod(ref IniData sCompilersConfig, ref ProcessStartInfo startInfo, string filePath)
        {
            try
            {

                // Устанавливаем имя запускаемой программы
                startInfo.FileName = filePath;

                // Аргументы запуска данной программы
                startInfo.Arguments = "";

            }
            catch
            {

                // В случае ошибки указываем, что работа
                // была выполнена не успешно.
                return false;

            }
            
            // Возвращаем родителю информацию о том,
            // что запашиваемая операция была выполнена
            // самым успешным образом.
            return true;

        }
        
    }

}
