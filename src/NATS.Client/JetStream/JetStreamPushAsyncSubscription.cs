﻿// Copyright 2021 The NATS Authors
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at:
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

namespace NATS.Client.JetStream
{
    public class JetStreamPushAsyncSubscription : AsyncSubscription, IJetStreamPushAsyncSubscription
    {
        internal readonly MessageManager[] messageManagers;
        public JetStream Context { get; }
        public string Stream { get; }
        public string Consumer { get; internal set; }
        public string DeliverSubject { get; }

        internal JetStreamPushAsyncSubscription(Connection conn, string subject, string queue,
            JetStream js, string stream, string consumer, string deliver, MessageManager[] messageManagers)
            : base(conn, subject, queue)
        {
            Context = js;
            Stream = stream;
            Consumer = consumer; // might be null, someone will call set on ConsumerName
            DeliverSubject = deliver;

            this.messageManagers = messageManagers;
            MessageManager.Startup(this, messageManagers);
        }

        public ConsumerInfo GetConsumerInformation() => Context.LookupConsumerInfo(Stream, Consumer);
        public bool IsPullMode() => false;
        
        public override void Unsubscribe()
        {
            MessageManager.Shutdown(messageManagers);
            base.Unsubscribe();
        }

        internal override void close()
        {
            MessageManager.Shutdown(messageManagers);
            base.close();
        }
    }
}
