﻿using Puppet.Common.Services;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Puppet.Common.Devices
{
    public class Speaker : DeviceBase   
    {
        public Speaker(HomeAutomationPlatform hub, string id) : base(hub, id)
        {
        }

        public void Speak(string message)
        {
            Console.WriteLine($"{DateTime.Now} Speaker {this.Id} speaking: {message}");
            _hub.DoAction(this, "speak", new string[] { message });
        }
    }
}
