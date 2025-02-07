﻿// Copyright 2015-2018 The NATS Authors
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Threading;
using System.Reflection;
using System.Linq;
using NATS.Client;
using UnitTests;
using Xunit;
using Xunit.Abstractions;

namespace IntegrationTests
{
    /// <summary>
    /// Run these tests with the gnatsd auth.conf configuration file.
    /// </summary>
    public class TestAuthorization : TestSuite<AuthorizationSuiteContext>
    {
        public TestAuthorization(AuthorizationSuiteContext context) : base(context)
        {
        }

        int hitDisconnect;

        private void ConnectShouldFail(string url)
        {
            try
            {
                hitDisconnect = 0;
                Options opts = Context.GetTestOptions();
                opts.Url = url;
                opts.DisconnectedEventHandler += HandleDisconnect;
                using (var c = Context.ConnectionFactory.CreateConnection(opts))
                {
                    c.Close();
                    
                    Assert.True(false, "Expected a failure; did not receive one");
                }
            }
            catch (Exception e)
            {
                Assert.Contains("Authorization", e.Message);
            }
            finally
            {
                Assert.False(hitDisconnect > 0, "hitDisconnect > 0: The disconnect event handler was incorrectly invoked.");
            }
        }

        private void HandleDisconnect(object sender, ConnEventArgs e)
        {
            hitDisconnect++;
        }

        private void ConnectShouldSucceed(string url)
        {
            using (var c = Context.ConnectionFactory.CreateConnection(url))
            {
                c.Close();
            }
        }

        [Fact]
        public void TestAuthSuccess()
        {
            using (NATSServer.CreateWithConfig(Context.Server1.Port, "auth.conf"))
            {
                ConnectShouldSucceed($"nats://username:password@localhost:{Context.Server1.Port}");
            }
        }

        [Fact]
        public void TestAuthFailure()
        {
            using (NATSServer.CreateWithConfig(Context.Server1.Port, "auth.conf"))
            {
                ConnectShouldFail($"nats://username@localhost:{Context.Server1.Port}");
                ConnectShouldFail($"nats://username:badpass@localhost:{Context.Server1.Port}");
                ConnectShouldFail(Context.Server1.Url);
                ConnectShouldFail($"nats://badname:password@localhost:{Context.Server1.Port}");
            }
        }

        [Fact]
        public void TestEncodedPassword()
        {
            using (NATSServer.CreateWithConfig(Context.Server1.Port, "encoded_pass.conf"))
            {
                void connectEncoded(string encoded)
                {
                    ConnectShouldSucceed($"nats://u{encoded}:p{encoded}@localhost:{Context.Server1.Port}");
                }

                connectEncoded("space%20space");
                connectEncoded("colon%3Acolon");
                connectEncoded("colon%3acolon"); // just making sure lower case hex
                connectEncoded("quote%27quote");
                connectEncoded("slash%2Fslash");
                connectEncoded("question%3Fquestion");
                connectEncoded("pound%23pound");
                connectEncoded("sqleft%5Bsqleft");
                connectEncoded("sqright%5Dsqright");
                connectEncoded("at%40at");
                connectEncoded("bang%21bang");
                connectEncoded("dollar%24dollar");
                connectEncoded("amp%26amp");
                connectEncoded("comma%2Ccomma");
                connectEncoded("parenleft%28parenleft");
                connectEncoded("parentright%29parentright");
                connectEncoded("asterix%2Aasterix");
                connectEncoded("plus%2Bplus");
                connectEncoded("semi%3Bsemi");
                connectEncoded("eq%3Deq");
                connectEncoded("pct%25pct");
#if !NET46
                connectEncoded("%2b%3a%c2%a1%c2%a2%c2%a3%c2%a4%c2%a5%c2%a6%c2%a7%c2%a8%c2%a9%c2%aa%c2%ab%c2%ac%20%f0%9f%98%80");
#endif
                // a plus sign in a user or pass is a plus sign, not a space
                Assert.Throws<NATSConnectionException>(() => connectEncoded("space+space"));
            }
        }

        [Fact]
        public void TestAuthToken()
        {
            using (NATSServer.Create(Context.Server1.Port, "-auth S3Cr3T0k3n!"))
            {
                ConnectShouldFail(Context.Server1.Url);
                ConnectShouldFail($"nats://invalid_token@localhost:{Context.Server1.Port}");

                Context.ConnectionFactory.CreateConnection($"nats://S3Cr3T0k3n!@localhost:{Context.Server1.Port}").Close();
            }
        }


        [Fact]
        public void TestReconnectAuthTimeout()
        {
            AutoResetEvent ev  = new AutoResetEvent(false);

            using (NATSServer s1 = NATSServer.CreateWithConfig(Context.Server1.Port, "auth.conf"),
                              _  = NATSServer.CreateWithConfig(Context.Server2.Port, "auth_timeout.conf"),
                              __ = NATSServer.CreateWithConfig(Context.Server3.Port, "auth.conf"))
            {

                Options opts = Context.GetTestOptions();

                opts.Servers = new []{
                    $"nats://username:password@localhost:{Context.Server1.Port}",
                    $"nats://username:password@localhost:{Context.Server2.Port}",
                    $"nats://username:password@localhost:{Context.Server3.Port}" };
                opts.NoRandomize = true;

                opts.ReconnectedEventHandler += (sender, args) =>
                {
                    ev.Set();
                };

                using (Context.ConnectionFactory.CreateConnection(opts))
                {
                    s1.Shutdown();

                    // This should fail over to S2 where an authorization timeout occurs
                    // then successfully reconnect to S3.

                    Assert.True(ev.WaitOne(20000));
                }
            }
        }

        [Fact]
        public void TestCallbackIsPerformedOnAuthFailure()
        {
            var cbEvent = new AutoResetEvent(false);
            var opts = Context.GetTestOptionsWithDefaultTimeout(Context.Server1.Port);
            opts.Url = $"nats://username:badpass@localhost:{Context.Server1.Port}";

            opts.AsyncErrorEventHandler += (sender, args) =>
            {
                cbEvent.Set();
            };

            using (NATSServer.CreateWithConfig(Context.Server1.Port, "auth.conf"))
            {
                var ex = Assert.Throws<NATSConnectionException>(() =>
                {
                    using (Context.ConnectionFactory.CreateConnection(opts)) { }
                });
                Assert.Equal("'Authorization Violation'", ex.Message, StringComparer.OrdinalIgnoreCase);
            }

            Assert.True(cbEvent.WaitOne(1000));
        }

        [Fact]
        public void TestExpiredJwt()
        {
            var expiredUserJwt 
                = "eyJ0eXAiOiJqd3QiLCJhbGciOiJlZDI1NTE5In0.eyJleHAiOjE1NDg5NzkyMDAs" +
                  "Imp0aSI6IlhURFdZUVc3QldDNzJSR0RaVzNWMlNGQUxFRklCWlRKRkZLWDRTVEpa" +
                  "TVZYWFFBSk01WVEiLCJpYXQiOjE1NzM1NDMyNjYsImlzcyI6IkFBNTVENUw1S0sz" +
                  "WElJNklLSDc0Vk5CUDNTVjNKWUxVQlRKTkxTVEM2NjJKTDZWN0FPWk9GT0NIIiwi" +
                  "bmFtZSI6IlRlc3RVc2VyIiwibmJmIjoxNTQ2MzAwODAwLCJzdWIiOiJVRDZPVUNS" +
                  "T1VEQTZBTTdZMjMySTRLTFVGWU40TTNPWUxJWFhVU0FNTzVQT1RVMkpaVjNVNzY3" +
                  "SiIsInR5cGUiOiJ1c2VyIiwibmF0cyI6eyJwdWIiOnt9LCJzdWIiOnt9fX0.n81V" +
                  "bNLwtYMRYfUDbLgnn0MzFL3imxlEk0PQSzOxQpB_nBkVKvRUtbnd22iS8S9i_HRO" +
                  "FJXfk26xEoOhYtCACg";

            var userSeed = "SUAIBDPBAUTWCWBKIO6XHQNINK5FWJW4OHLXC3HQ2KFE4PEJUA44CNHTC4A";
    
            using (NATSServer.CreateWithConfig(Context.Server1.Port, "operator.conf"))
            {
                EventHandler<UserJWTEventArgs> jwtEh = (sender, args) => args.JWT = expiredUserJwt;
                EventHandler<UserSignatureEventArgs> sigEh = (sender, args) =>
                {
                    // generate a nats key pair from a private key.
                    // NEVER EVER handle a real private key/seed like this.
                    var kp = Nkeys.FromSeed(userSeed);
                    args.SignedNonce = kp.Sign(args.ServerNonce);
                };
                var opts = Context.GetTestOptionsWithDefaultTimeout(Context.Server1.Port);
                opts.SetUserCredentialHandlers(jwtEh, sigEh);

                var ex = Assert.Throws<NATSConnectionException>(() =>
                {
                    using(Context.ConnectionFactory.CreateConnection(opts)){ }
                });

                Assert.Equal("'Authorization Violation'", ex.Message, StringComparer.OrdinalIgnoreCase);
            }
        }

#if NET46
        [Fact]
        public void TestReconnectAuthTimeoutLateClose()
        {
            AutoResetEvent ev = new AutoResetEvent(false);

            using (NATSServer s1 = NATSServer.CreateWithConfig(Context.Server1.Port, "auth.conf"),
                              _  = NATSServer.CreateWithConfig(Context.Server3.Port, "auth.conf"))
            {

                Options opts = Context.GetTestOptions();

                opts.Servers = new [] {
                    $"nats://username:password@localhost:{Context.Server1.Port}",
                    $"nats://username:password@localhost:{Context.Server3.Port}" };
                opts.NoRandomize = true;

                opts.ReconnectedEventHandler += (sender, args) =>
                {
                    ev.Set();
                };

                using (var c = Context.ConnectionFactory.CreateConnection(opts))
                {
                    // inject an authorization timeout, as if it were processed by an incoming server message.
                    // this is done at the parser level so that parsing is also tested,
                    // therefore it needs reflection since Parser is an internal type.
                    Type parserType = typeof(Connection).Assembly.GetType("NATS.Client.Parser");
                    Assert.NotNull(parserType);

                    BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
                    object parser = Activator.CreateInstance(parserType, flags, null, new object[] {c}, null);
                    Assert.NotNull(parser);


                    MethodInfo parseMethod = parserType.GetMethod("parse", flags);
                    Assert.NotNull(parseMethod);

                    byte[] bytes = "-ERR 'Authorization Timeout'\r\n".ToCharArray().Select(ch => (byte) ch).ToArray();
                    parseMethod.Invoke(parser, new object[] {bytes, bytes.Length});

                    // sleep to allow the client to process the error, then shutdown the server.
                    Thread.Sleep(250);

                    s1.Shutdown();

                    // Wait for a reconnect.
                    Assert.True(ev.WaitOne(20000));
                }
            }
        }
#endif
    }
}
