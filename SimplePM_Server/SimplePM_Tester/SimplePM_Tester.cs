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

using System.Collections.Generic;
using CompilerBase;
using MySql.Data.MySqlClient;

namespace SimplePM_Server.SimplePM_Tester
{

    /*
     * Класс тестирования пользовательских
     * решений   поставленных   задач   по
     * программированию.
     */
    internal partial class SimplePM_Tester
    {

        #region Секция объявления глобальных переменных
        private SubmissionInfo.SubmissionInfo submissionInfo; // ссылка на объект, содержащий информацию о запросе на тестирование
        private List<ICompilerPlugin> _compilerPlugins; // ссылка на список модулей компиляции
        private MySqlConnection connection; // ссылка на дескриптор соединения к базе данных
        private readonly string exeFileUrl; // переменная, содержащая полный путь к exe-файлу пользовательского решения
        private dynamic _serverConfiguration;
        private dynamic _languageConfiguration;
        #endregion

        public SimplePM_Tester(
            ref MySqlConnection connection,
            ref dynamic serverConfiguration,
            ref dynamic languageConfiguration,
            ref List<ICompilerPlugin> _compilerPlugins,
            ref string exeFileUrl,
            ref SubmissionInfo.SubmissionInfo submissionInfo
        )
        {

            this.connection = connection;
            _serverConfiguration = serverConfiguration;
            _languageConfiguration = languageConfiguration;
            this._compilerPlugins = _compilerPlugins;
            this.exeFileUrl = exeFileUrl;
            this.submissionInfo = submissionInfo;

        }

    }

}
