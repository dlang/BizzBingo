﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using BizzBingo.Web.Infrastructure.Raven;
using BizzBingo.Web.Models;
using Raven.Client.Linq;

namespace BizzBingo.Web.Infrastructure.DotNetOAuth
{
    using System.Configuration;
    using System.Globalization;
    using System.IO;
    using System.Net;
    using System.Xml;
    using System.Xml.Linq;
    using System.Xml.XPath;
    using DotNetOpenAuth.Messaging;
    using DotNetOpenAuth.OAuth;
    using DotNetOpenAuth.OAuth.ChannelElements;

    /// <summary>
    /// A consumer capable of communicating with Twitter.
    /// </summary>
    public static class TwitterConsumer
    {
        /// <summary>
        /// The description of Twitter's OAuth protocol URIs for use with actually reading/writing
        /// a user's private Twitter data.
        /// </summary>
        public static readonly ServiceProviderDescription ServiceDescription = new ServiceProviderDescription
        {
            RequestTokenEndpoint = new MessageReceivingEndpoint("http://twitter.com/oauth/request_token", HttpDeliveryMethods.GetRequest | HttpDeliveryMethods.AuthorizationHeaderRequest),
            UserAuthorizationEndpoint = new MessageReceivingEndpoint("http://twitter.com/oauth/authorize", HttpDeliveryMethods.GetRequest | HttpDeliveryMethods.AuthorizationHeaderRequest),
            AccessTokenEndpoint = new MessageReceivingEndpoint("http://twitter.com/oauth/access_token", HttpDeliveryMethods.GetRequest | HttpDeliveryMethods.AuthorizationHeaderRequest),
            TamperProtectionElements = new ITamperProtectionChannelBindingElement[] { new HmacSha1SigningBindingElement() },
        };

        /// <summary>
        /// The description of Twitter's OAuth protocol URIs for use with their "Sign in with Twitter" feature.
        /// </summary>
        public static readonly ServiceProviderDescription SignInWithTwitterServiceDescription = new ServiceProviderDescription
        {
            RequestTokenEndpoint = new MessageReceivingEndpoint("http://twitter.com/oauth/request_token", HttpDeliveryMethods.GetRequest | HttpDeliveryMethods.AuthorizationHeaderRequest),
            UserAuthorizationEndpoint = new MessageReceivingEndpoint("http://twitter.com/oauth/authenticate", HttpDeliveryMethods.GetRequest | HttpDeliveryMethods.AuthorizationHeaderRequest),
            AccessTokenEndpoint = new MessageReceivingEndpoint("http://twitter.com/oauth/access_token", HttpDeliveryMethods.GetRequest | HttpDeliveryMethods.AuthorizationHeaderRequest),
            TamperProtectionElements = new ITamperProtectionChannelBindingElement[] { new HmacSha1SigningBindingElement() },
        };

        /// <summary>
        /// The URI to get a user's favorites.
        /// </summary>
        private static readonly MessageReceivingEndpoint GetFavoritesEndpoint = new MessageReceivingEndpoint("http://twitter.com/favorites.xml", HttpDeliveryMethods.GetRequest);

        /// <summary>
        /// The URI to get the data on the user's home page.
        /// </summary>
        private static readonly MessageReceivingEndpoint VerifyCredentialsEndpoint = new MessageReceivingEndpoint("http://api.twitter.com/1/account/verify_credentials.xml", HttpDeliveryMethods.GetRequest | HttpDeliveryMethods.AuthorizationHeaderRequest);

        /// <summary>
        /// The consumer used for the Sign in to Twitter feature.
        /// </summary>
        private static WebConsumer signInConsumer;

        /// <summary>
        /// The lock acquired to initialize the <see cref="signInConsumer"/> field.
        /// </summary>
        private static object signInConsumerInitLock = new object();

        /// <summary>
        /// Initializes static members of the <see cref="TwitterConsumer"/> class.
        /// </summary>
        static TwitterConsumer()
        {
            // Twitter can't handle the Expect 100 Continue HTTP header. 
            ServicePointManager.FindServicePoint(GetFavoritesEndpoint.Location).Expect100Continue = false;
        }

        /// <summary>
        /// Gets a value indicating whether the Twitter consumer key and secret are set in the web.config file.
        /// </summary>
        public static bool IsTwitterConsumerConfigured
        {
            get
            {
                return !string.IsNullOrEmpty(ConfigurationManager.AppSettings["twitterConsumerKey"]) &&
                    !string.IsNullOrEmpty(ConfigurationManager.AppSettings["twitterConsumerSecret"]);
            }
        }

        /// <summary>
        /// Gets the consumer to use for the Sign in to Twitter feature.
        /// </summary>
        /// <value>The twitter sign in.</value>
        private static WebConsumer TwitterSignIn
        {
            get
            {
                if (signInConsumer == null)
                {
                    lock (signInConsumerInitLock)
                    {
                        if (signInConsumer == null)
                        {
                            signInConsumer = new WebConsumer(SignInWithTwitterServiceDescription, RavenDbTokenManager);
                        }
                    }
                }

                return signInConsumer;
            }
        }

        private static InMemoryTokenManager RavenDbTokenManager
        {
            get
            {
                var store = HttpContext.Current.Session;
                var tokenManager = (InMemoryTokenManager)store["InMemoryTokenManagerUserSessionTokenManager"];
                if (tokenManager == null)
                {
                    string consumerKey = ConfigurationManager.AppSettings["twitterConsumerKey"];
                    string consumerSecret = ConfigurationManager.AppSettings["twitterConsumerSecret"];
                    if (IsTwitterConsumerConfigured)
                    {
                        tokenManager = new InMemoryTokenManager(consumerKey, consumerSecret);
                        store["InMemoryTokenManagerUserSessionTokenManager"] = tokenManager;
                    }
                    else
                    {
                        throw new InvalidOperationException("No Twitter OAuth consumer key and secret could be found in web.config AppSettings.");
                    }
                }

                return tokenManager;
            }
        }

        public static XDocument VerifyCredentials(ConsumerBase twitter, string accessToken)
        {
            IncomingWebResponse response = twitter.PrepareAuthorizedRequestAndSend(VerifyCredentialsEndpoint, accessToken);
            return XDocument.Load(XmlReader.Create(response.GetResponseReader()));
        }

        public static string GetUsername(ConsumerBase twitter, string accessToken)
        {
            XDocument xml = VerifyCredentials(twitter, accessToken);
            XPathNavigator nav = xml.CreateNavigator();
            return nav.SelectSingleNode("/user/screen_name").Value;
        }

        /// <summary>
        /// Prepares a redirect that will send the user to Twitter to sign in.
        /// </summary>
        /// <param name="forceNewLogin">if set to <c>true</c> the user will be required to re-enter their Twitter credentials even if already logged in to Twitter.</param>
        /// <returns>The redirect message.</returns>
        /// <remarks>
        /// Call <see cref="OutgoingWebResponse.Send"/> or
        /// <c>return StartSignInWithTwitter().<see cref="MessagingUtilities.AsActionResult">AsActionResult()</see></c>
        /// to actually perform the redirect.
        /// </remarks>
        public static OutgoingWebResponse StartSignInWithTwitter(bool forceNewLogin)
        {
            var redirectParameters = new Dictionary<string, string>();
            if (forceNewLogin)
            {
                redirectParameters["force_login"] = "true";
            }
            Uri callback = MessagingUtilities.GetRequestUrlFromContext().StripQueryArgumentsWithPrefix("oauth_");
            var request = TwitterSignIn.PrepareRequestUserAuthorization(callback, null, redirectParameters);
            return TwitterSignIn.Channel.PrepareResponse(request);
        }

        /// <summary>
        /// Checks the incoming web request to see if it carries a Twitter authentication response,
        /// and provides the user's Twitter screen name and unique id if available.
        /// </summary>
        /// <param name="screenName">The user's Twitter screen name.</param>
        /// <param name="userId">The user's Twitter unique user ID.</param>
        /// <returns>
        /// A value indicating whether Twitter authentication was successful;
        /// otherwise <c>false</c> to indicate that no Twitter response was present.
        /// </returns>
        public static bool TryFinishSignInWithTwitter(out string screenName, out int userId, out string accessToken)
        {
            screenName = null;
            userId = 0;
            accessToken = null;
            var response = TwitterSignIn.ProcessUserAuthorization();
            if (response == null)
            {
                return false;
            }

            screenName = response.ExtraData["screen_name"];
            userId = int.Parse(response.ExtraData["user_id"]);
            accessToken = response.AccessToken;

            // If we were going to make this LOOK like OpenID even though it isn't,
            // this seems like a reasonable, secure claimed id to allow the user to assume.
            DotNetOpenAuth.OpenId.Identifier fake_claimed_id = string.Format(CultureInfo.InvariantCulture, "http://twitter.com/{0}#{1}", screenName, userId);

            return true;
        }
    }
}