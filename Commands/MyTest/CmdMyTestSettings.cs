﻿using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Text;
using DSharpPlus;
using DSharpPlus.Entities;
using MyTestLib;

namespace MePhIt.Commands.MyTest
{
    /// <summary>
    /// Local settings for MyTest command group
    /// </summary>
    public class CmdMyTestSettings
    {
        public TestState TestState { get; set; } = null;
        public IDictionary<DiscordMember, TestResults> TestResults { get; set; } = new Dictionary<DiscordMember, TestResults>();

        /// <summary>
        /// Test channel group
        /// </summary>
        public IDictionary<DiscordMember, DiscordChannel> TempTestChannelGrp { get; set; } = new ConcurrentDictionary<DiscordMember, DiscordChannel>();
        /// <summary>
        /// Test question messages
        /// </summary>
        public IDictionary<DiscordMember, IList<DiscordMessage>> TempTestChannelQuestions { get; set; } = new ConcurrentDictionary<DiscordMember, IList<DiscordMessage>>();
        /// <summary>
        /// Timers
        /// </summary>
        public MyTestTimer Timer { get; set; } = null;
    }
}