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

namespace SimplePM_Server.SimplePM_Tester
{

    /*
     * Класс, который является примитивом
     * единичного     теста     заданного
     * пользовательского  решения  данной
     * задачи по программированию.
     */

    internal class TestResult
    {

        /*
         * Раздел объявления важных констант
         */

        public const char MiddleSuccessResult = '*';
        public const char TimeLimitResult = 'T';
        public const char MemoryLimitResult = 'M';
        public const char RuntimeErrorResult = 'R';
        public const char OutputErrorResult = 'O';
        
        /*
         * Раздел объявления переменных
         */

        public string ErrorOutput       { get; set; } // Выходной поток ошибок пользовательской программы
        public string Output            { get; set; } // Выходной поток пользовательской программы

        public int    ExitCode          { get; set; } // Код выхода пользовательской программы
        public char   Result            { get; set; } // Результат по текущему тесту

        public int    UsedProcessorTime { get; set; } // Использование процессорного времени пользовательской программой
        public long   UsedMemory        { get; set; } // Использование памяти пользовательской программой
        
        public bool   IsSuccessful      => (
            Result == '+' && ExitCode == 0
        ); // Указывает на то, пройден ли тест или нет

        /*
         * Различные вариации конструкторов
         */
        public TestResult()
        {

        }

        public TestResult(
            char Result,
            int ExitCode,
            long UsedMemory,
            int UsedProcessorTime,
            string ErrorOutput,
            string Output
        )
        {

            /* Присвоение каким-то переменным каких-то значений */
            this.Result = Result;
            this.ExitCode = ExitCode;
            this.UsedMemory = UsedMemory;
            this.UsedProcessorTime = UsedProcessorTime;
            this.ErrorOutput = ErrorOutput;
            this.Output = Output;

        }
        
    }

}