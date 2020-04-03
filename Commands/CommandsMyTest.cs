﻿using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Timers;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using static MePhIt.MePhItUtilities;
using MePhIt.Commands.MyTest;

using MyTestLib;

using System.Linq;

namespace MePhIt.Commands
{

    [Group("mytest")]
    [
        Aliases("mt",
                "тест")
    ]
    [Description("Управление поведением MyTest")]
    [RequirePermissions(Permissions.Administrator)]
    public class CommandsMyTest : BaseCommandModule
    {
        // Shortcuts
        private MePhItBot Bot = MePhItBot.Bot;
        private MePhItSettings BotSettings = MePhItBot.Bot.Settings;
        private MePhItLocalization Localization = MePhItBot.Bot.Settings.Localization;

        // Local Settings
        public IDictionary<DiscordGuild, TestState> TestLoaded = new ConcurrentDictionary<DiscordGuild, TestState>();
        public IDictionary<DiscordGuild, CmdMyTestSettings> Settings = new ConcurrentDictionary<DiscordGuild, CmdMyTestSettings>();

        // ------- Time settings -------
        // Timeout before the test starts
        private int timeoutBeforeTestMinutes = 2;
        private string commandTestFinish = "finish";

        // Question text format
        private string questionTextFormat = ":question: **{0}**. *{1}?*";
        // Answer text format
        private string answerTextFormat = "{0} {1}";
        
        /// <summary>
        /// List available test files
        /// </summary>
        /// <param name="commandContext"></param>
        /// <param name="channel">Channel where to show findings</param>
        /// <returns></returns>
        [Command("list")]
        [Aliases("список")]
        [Description("Показать доступные файлы тестов")]
        [RequirePermissions(Permissions.Administrator)]
        public async Task MyTestList(CommandContext commandContext, DiscordChannel channel = null)
        {
            channel = channel == null ? commandContext.Channel : channel;

            try
            {
                var msg = Localization.Message(commandContext.Guild, MessageID.CmdMyTestTestsSearch) + "\n";

                // List files in nested directories
                var availableTests = new List<string>(Directory.GetFiles(BotSettings.MyTestFolder, "*.mtc", SearchOption.AllDirectories));

                // Show found test files
                if (availableTests.Count == 0)
                {
                    msg = $"{msg} {Localization.Message(commandContext.Guild, MessageID.CmdMyTestTestsNotFound)}";
                }
                else
                {
                    // Convert filepaths to Unix
                    foreach (var test in availableTests)
                    {
                        var relPath = Path.GetRelativePath(BotSettings.MyTestFolder, test).Split(Path.DirectorySeparatorChar);
                        string relPathFixed = "";
                        foreach(var rPath in relPath)
                        {
                            var sep = rPath != relPath[relPath.Length - 1] ? Path.AltDirectorySeparatorChar.ToString() : "";
                            relPathFixed += $"{rPath}{sep}";
                        }
                        msg = $"{msg}{relPathFixed}\n";
                    }
                }

                await channel.SendMessageAsync(msg);
                commandContext.Message.CreateReactionAsync(Bot.ReactSuccess);
            }
            catch(Exception e)
            {
                await channel.SendMessageAsync($":bangbang: {e.Message}");
                commandContext.Message.CreateReactionAsync(Bot.ReactFail);
            }
        }

        /// <summary>
        /// Select specific test file
        /// </summary>
        /// <param name="commandContext"></param>
        /// <returns></returns>
        [Command("file")]
        [Aliases("файл")]
        [Description("Выбрать файл теста из установленных в системе")]
        [RequirePermissions(Permissions.Administrator)]
        public async Task MyTestFile(CommandContext commandContext, params string[] filepath)
        {
            var filePath = "";
            foreach (var str in filepath)
            {
                filePath += str + (str != filepath[filepath.Length - 1] ? " " : "");
            }

            try
            {
                // Load test
                filePath = Path.Combine(BotSettings.MyTestFolder, filePath);
                var test = new TestState();
                test.LoadTest(filePath);
                if(!TestLoaded.TryAdd(commandContext.Guild, test))
                {
                    throw new FileLoadException($"Unable to load test from {filePath}");
                }

                Settings.TryAdd(commandContext.Guild, new CmdMyTestSettings());
                var msg = string.Format(Localization.Message(commandContext.Guild, MessageID.CmdMyTestFileLoadSuccess), test.Name);
                await commandContext.Channel.SendMessageAsync(msg);
                commandContext.Message.CreateReactionAsync(Bot.ReactSuccess);
            }
            catch(Exception e)
            {
                await commandContext.Channel.SendMessageAsync($"{EmojiError} {e.Message}");
                commandContext.Message.CreateReactionAsync(Bot.ReactFail);
            }
        }

        [Command("start")]
        [Aliases("старт")]
        [Description("Начать тестирование")]
        [RequirePermissions(Permissions.Administrator)]
        public async Task MyTestStart(CommandContext commandContext, DiscordChannel channelInfoMessages = null)
        {
            channelInfoMessages = channelInfoMessages == null ? commandContext.Channel : channelInfoMessages;

            // 1. Get loaded test
            TestState test = TestLoaded[commandContext.Guild]; ;
            CmdMyTestSettings settings;

            if(!Settings.TryGetValue(commandContext.Guild, out settings) || settings.TestState == null)
            {
                await commandContext.Message.CreateReactionAsync(MePhItBot.Bot.ReactFail);
                return;
            }

            // 2. Get a list of students online
            var students = await GetStudentsOnlineAsync(commandContext.Guild);
            foreach(var student in students)
            {
                Settings[commandContext.Guild].TestState[student as DiscordMember] = test.Clone() as TestState;
            }

            // 3. Create test channels personal for each student
            // Permission to read only for this specific student
            // 3.1. Create MyTest channel category
            // 3.2. Create text channels with names corresponding to student's displayed name
            // 3.3 Store created temporary channels
            Settings[commandContext.Guild].TempTestChannelGrp = await CreateTemporaryTestChannelGroup(commandContext, students);
            var tempChannels = Settings[commandContext.Guild].TempTestChannelGrp;

            // 3.4. Inform students to join their corresponding channels
            var channelMentions = "";
            foreach (var mention in GetChannelMentions(tempChannels.Values))
            {
                channelMentions += mention;
            }
            // Generic test info
            var msg = string.Format(
                                    Localization.Message(commandContext.Guild, MessageID.CmdMyTestStartHelp),
                                    channelMentions,
                                    BotSettings.Discord.CurrentUser.Mention,
                                    commandTestFinish,
                                    timeoutBeforeTestMinutes
                                    ) + "\n";
            await channelInfoMessages.SendMessageAsync($"{commandContext.Guild.EveryoneRole.Mention}\n{msg}");


            // Test name and time
            msg = $"{EmojiInfo} {test.Name}\n";
            var testTime = test.Time != TestState.TimeInfinite ? new TimeSpan(0, 0, test.Time).TotalMinutes.ToString() : "∞";
            msg += string.Format(Localization.Message(commandContext.Guild, MessageID.CmdMyTestStartTime), testTime) + "\n";
            foreach (var tempChannel in tempChannels)
            {
                tempChannel.Value.SendMessageAsync($"{tempChannel.Key.Mention}\n{msg}");
            }

            // 4. Register callback for test start/stop event
            try
            {
                var mtTimer = new MyTestTimer(this, new TimeSpan(0, timeoutBeforeTestMinutes, 0).TotalMilliseconds);
                Settings[commandContext.Guild].Timer = mtTimer;
                mtTimer.Elapsed += Timer_Elapsed;
                mtTimer.Start();
            }
            catch(Exception e)
            {
                channelInfoMessages.SendMessageAsync($"{EmojiError} {e.Message}");
                return;
            }

            // 5. Register test start to enable the *ready* command from the student
            commandContext.Message.CreateReactionAsync(Bot.ReactSuccess);
        }

        /// <summary>
        /// Retrieve server of the timer
        /// </summary>
        /// <param name="timer">Timer</param>
        /// <returns></returns>
        internal DiscordGuild GetServer(MyTestTimer timer)
        {
            foreach(var srvr in Settings)
            {
                if(srvr.Value.Timer == timer)
                {
                    return srvr.Key;
                }
            }
            return null;
        }

        /// <summary>
        /// Test timer event handler
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            var timer = sender as MyTestTimer;
            if(!timer.TestStarted)
            {// Pre-test event
                // Start pre-test timer
                timer.StartTest();
                // 6. Throw all of the questions at student's channels
                // Remember message IDs to read student's answers from them
                timer.CommandsMyTest.SendTestQuestionsAsync(timer.CommandsMyTest.GetServer(timer));
            }
            else
            {// Post-test event

                timer.Stop();
                // 7. At the end of the test collect all the reactions from the students 
                // and process them as answers
                var cmt = timer.CommandsMyTest;
                var srvr = cmt.GetServer(timer);
                var settings = cmt.Settings[srvr];

                var students = settings.TempTestChannelQuestions.Keys;

                var testResults = await CollectAnswersAsync(settings, students);


                // 8. Present the question-answer statistics to the students
                var msg = Bot.Settings.Localization.Message(srvr, MessageID.CmdMyTestStartTestFinished);
                var msgQuestionStrFormat = Bot.Settings.Localization.Message(srvr, MessageID.CmdMyTestStartTestQuestionResult);
                var msgTestResultsStrFormat = Bot.Settings.Localization.Message(srvr, MessageID.CmdMyTestStartTestTotalResults);

                foreach(var student in students)
                {
                    var studentResult = testResults.First(sr => sr.Student == student).Results;
                    var messages = settings.TempTestChannelQuestions[student];

                    SendTestResultStatisticsAsync(settings.TempTestChannelGrp[student], messages, studentResult);
                }

                // 9. Present the question-answer statistics to the teacher

                // 10. Form the marks earned
                
            }
        }

        private async Task SendTestResultStatisticsAsync(DiscordChannel studentChannel, IList<(TestQuestion Question, ulong MessageId)> messages, TestResults studentResults)
        {
            // ---- HEADER ----
            var msg = $"{Bot.Settings.Localization.Message(studentChannel.Guild, MessageID.CmdMyTestStartTestFinished)}\n";

            // ---- QUESTIONS ----
            var questions = studentResults.TestState.Questions;
            foreach (var question in questions)
            {
                foreach(var message in messages)
                {
                    if(message.Question == question)
                    {
                        var dmsg = await studentChannel.GetMessageAsync(message.MessageId);
                        var uri = dmsg.JumpLink;
                        var strQuestionFormat = Bot.Settings.Localization.Message(studentChannel.Guild, MessageID.CmdMyTestStartTestQuestionResult);
                        var questionNumber = questions.IndexOf(question) + 1;
                        var score = studentResults.QuestionScore(question);
                        var accuracy = studentResults.QuestionScore(question) / question.Value;
                        accuracy = accuracy > 0 ? accuracy : 0;
                        msg += string.Format(strQuestionFormat, questionNumber, uri, accuracy, score);
                    }
                }
            }

            // ---- SUMMARY ----
            var strSummaryFormat = Bot.Settings.Localization.Message(studentChannel.Guild, MessageID.CmdMyTestStartTestTotalResults);
            var totalAccuracy = studentResults.Score / studentResults.TestState.Value;
            var mark = GetMark(studentChannel.Guild, totalAccuracy);
            msg += string.Format(strSummaryFormat, totalAccuracy, studentResults.Score, studentResults.TestState.Value, mark.Mark, mark.NationalMark, mark.ECTS) ;

            studentChannel.SendMessageAsync(msg);
        }

        (string Mark, string NationalMark, int ECTS) GetMark(in DiscordGuild server, in double accuracy)
        {
            /*
             * >= 90    A       [90; 100]
             * >= 82    B       [82; 89]
             * >= 74    C       [74; 81]
             * >= 64    D       [64; 73]
             * >= 60    E       [60; 63]
             * >= 35    F       [35; 59]
             * >= 1     Fx      [1; 34]
             */
            var mark5scale = accuracy * 5;
            var markNational = "";
            var markECTS = (int)Math.Round(20 * mark5scale);
            var mark = "";

            if (mark5scale == 5)
            {
                markNational = Bot.Settings.Localization.Message(server, MessageID.CmdMyTestMark100);
            }
            var value = (int)Math.Round(mark5scale + 0.001);
            switch (value)
            {
                case 5:
                    mark = NumberToEmoji(5);
                    markNational = Bot.Settings.Localization.Message(server, MessageID.CmdMyTestMark5);
                    break;
                case 4:
                    mark = NumberToEmoji(4);
                    markNational = Bot.Settings.Localization.Message(server, MessageID.CmdMyTestMark4);
                    break;
                case 3:
                    mark = NumberToEmoji(3);
                    markNational = Bot.Settings.Localization.Message(server, MessageID.CmdMyTestMark3);
                    break;
                case 2:
                    mark = NumberToEmoji(2);
                    markNational = Bot.Settings.Localization.Message(server, MessageID.CmdMyTestMark2);
                    break;
            }
            if (value > 0 && value < 2)
            {
                mark = NumberToEmoji(1);
                markNational = Bot.Settings.Localization.Message(server, MessageID.CmdMyTestMark1);
            }
            if(value <= 0)
            {
                mark = NumberToEmoji(0);
                markNational = Bot.Settings.Localization.Message(server, MessageID.CmdMyTestMark0);
                markECTS = 0;
            }

            return (mark, markNational, markECTS);
        }

        /// <summary>
        /// Collect answers from the students
        /// </summary>
        /// <param name="settings"></param>
        /// <param name="student"></param>
        /// <returns></returns>
        private async Task<ICollection<(DiscordMember Student, TestResults Results)>> CollectAnswersAsync(CmdMyTestSettings settings, ICollection<DiscordMember> students)
        {
            var resultStatistics = new List<(DiscordMember Student, TestResults Results)>();

            var tasks = new List<Task<(DiscordMember Student, TestResults Results)>>();
            foreach (var student in students)
            {
                tasks.Add(CollectAnswersFromStudentAsync(settings, student));
            }

            foreach(var task in tasks)
            {
                resultStatistics.Add(await task);
            }

            return resultStatistics;
        }

        /// <summary>
        /// Collect answers from the student
        /// </summary>
        /// <param name="settings"></param>
        /// <param name="student"></param>
        /// <returns></returns>
        private async Task<(DiscordMember Student, TestResults Results)> CollectAnswersFromStudentAsync(CmdMyTestSettings settings, DiscordMember student)
        {
            foreach (var data in settings.TempTestChannelQuestions[student])
            {
                var answers = new List<TestAnswer>();
                // BEGIN ---- Convert reactions to answers ----
                var message = await settings.TempTestChannelGrp[student].GetMessageAsync(data.MessageId);

                foreach (var r in message.Reactions)
                {
                    // If this answer has been selected, then store it
                    if (r.Count == 2)
                    {
                        int i = EmojiToNumber(Bot.Settings.Discord, r.Emoji.Name);
                        var answer = data.Question.Answers.ElementAt(i - 1);
                        answers.Add(answer);
                    }
                }
                // END ---- Convert reactions to answers ----
                settings.TestState[student].Results.Answer(data.Question, answers);
            }
            return (student, settings.TestState[student].Results);
        }

        /// <summary>
        /// Sends all test questions and answers to them
        /// </summary>
        /// <param name="server"></param>
        /// <returns></returns>
        private async Task SendTestQuestionsAsync(DiscordGuild server)
        {
            var test = Settings[server].TestState;
            var tempChannels = Settings[server].TempTestChannelGrp;
            var testMessages = new Dictionary<DiscordMember, IList<(TestQuestion Question, ulong MessageId)>> ();
            // Send Questions to the test channels
            var sendTasks = new List<Task<(DiscordMember Student, IList<(TestQuestion Question, ulong MessageId)> Messages)>>();
            foreach (var tempChannel in tempChannels)
            {
                var task = SendTestQuestionsToStudentAsync(tempChannel.Key, tempChannel.Value);
                sendTasks.Add(task);
            }

            // Collect the messages sent for each student
            foreach(var task in sendTasks)
            {
                var taskResult = await task;
                testMessages[taskResult.Student] = taskResult.Messages;
            }

            Settings[server].TempTestChannelQuestions = testMessages;
        }

        private async Task<(DiscordMember Student, IList<(TestQuestion Question, ulong MessageId)> Messages)> 
            SendTestQuestionsToStudentAsync(DiscordMember student, DiscordChannel tempChannel)
        {
            var server = student.Guild;
            var test = Settings[server].TestState[student];
            test.Shuffle();
            var questions = test.Questions;
            var testMessages = new List<(TestQuestion Question, ulong MessageId)>();
            foreach (var question in questions)
            {
                //question.Shuffle(new Random(Task.CurrentId == null ? 0 : (int)Task.CurrentId));
                // Question
                var msg = string.Format($"{questionTextFormat}\n", questions.IndexOf(question) + 1, question.Text);
                // Answers
                foreach (var answer in question.Answers)
                {
                    msg += string.Format(answerTextFormat, NumberToEmoji(question.Answers.IndexOf(answer) + 1), answer.Text) + "\n";
                }
                
                var message = await tempChannel.SendMessageAsync(msg);
                System.Threading.Thread.Sleep(200);

                for (int i = 1; i <= question.Answers.Count; i++)
                {
                    await message.CreateReactionAsync(DiscordEmoji.FromName(BotSettings.Discord, NumberToEmoji(i)));
                    System.Threading.Thread.Sleep(50);
                }

                // Store messages for each student
                try
                {
                    testMessages.Add((question, message.Id));
                }
                catch (Exception)
                {
                    testMessages = new List<(TestQuestion Question, ulong MessageId)>();
                    testMessages.Add((question, message.Id));
                }
            }
            return (student, testMessages);
        }



        private async Task<IDictionary<DiscordMember, DiscordChannel>> 
            CreateTemporaryTestChannelGroup(CommandContext commandContext, IEnumerable<DiscordUser> students)
        {
            var tempCategoryName = Localization.Message(commandContext.Guild, MessageID.CmdMyTestStartTempCategoryName);
            var tempChannelNames = new List<(DiscordMember, bool)>();
            foreach (var student in students)
            {
                tempChannelNames.Add((student as DiscordMember, false));
            }

            var tempTestChannels = new Dictionary<DiscordMember, DiscordChannel>();

            var tempChannels = await CreateTemproraryChannelsAsync(commandContext.Guild, tempCategoryName, tempChannelNames);
            // Close channels to everyone except the tested person
            foreach (var tmpCh in tempChannels.NestedChannels)
            {
                await tmpCh.Channel.AddOverwriteAsync(commandContext.Guild.EveryoneRole, Permissions.None, Permissions.All);
                await tmpCh.Channel.AddOverwriteAsync(tmpCh.Member, Permissions.AccessChannels | Permissions.ReadMessageHistory, Permissions.All);
                tempTestChannels[tmpCh.Member] = tmpCh.Channel;
            }
            return tempTestChannels;
        }

        [Command("stop")]
        [Aliases("стоп")]
        [Description("Прекратить тестирование")]
        [RequirePermissions(Permissions.Administrator)]
        public async Task MyTestStop(CommandContext commandContext)
        {
            throw new NotImplementedException();
        }

        [Command("results")]
        [Aliases("результаты")]
        [Description("Показать результаты тестирования")]
        [RequirePermissions(Permissions.Administrator)]
        public async Task MyTestResults(CommandContext commandContext, bool brief = false)
        {
            throw new NotImplementedException();
        }
    }
}
