﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Azure.Messaging.EventHubs.Errors;
using Microsoft.Azure.Amqp;
using Microsoft.Azure.Amqp.Encoding;
using Microsoft.Azure.Amqp.Framing;

namespace Azure.Messaging.EventHubs
{
    /// <summary>
    ///   The set of well-known error codes associated with an AMQP messages and
    ///   entities.
    /// </summary>
    ///
    internal static class AmqpError
    {
        /// <summary>Indicates that a timeout occurred on the link.</summary>
        public static readonly AmqpSymbol TimeoutError = AmqpConstants.Vendor + ":timeout";

        /// <summary>Indicates that the server was busy and could not allow the requested operation.</summary>
        public static readonly AmqpSymbol ServerBusyError = AmqpConstants.Vendor + ":server-busy";

        /// <summary>Indicates that an argument provided to the Event Hubs service was incorrect.</summary>
        public static readonly AmqpSymbol ArgumentError = AmqpConstants.Vendor + ":argument-error";

        /// <summary>Indicates that an argument provided to the Event Hubs service was incorrect.</summary>
        public static readonly AmqpSymbol ArgumentOutOfRangeError = AmqpConstants.Vendor + ":argument-out-of-range";

        /// <summary>The status text that appears when an AMQP error was due to a missing resource.</summary>
        private const string NotFoundStatusText = "status-code: 404";

        /// <summary>The expression to test for when the service returns a "Not Found" response to determine the context.</summary>
        private static readonly Regex NotFoundExpression = new Regex("The messaging entity .* could not be found", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>The set of mappings from AMQP error conditions to response status codes.</summary>
        private static readonly IReadOnlyDictionary<AmqpResponseStatusCode, AmqpSymbol> StatusCodeMap = new Dictionary<AmqpResponseStatusCode, AmqpSymbol>()
        {
            { AmqpResponseStatusCode.NotFound, AmqpErrorCode.NotFound },
            { AmqpResponseStatusCode.NotImplemented, AmqpErrorCode.NotImplemented},
            { AmqpResponseStatusCode.Unauthorized, AmqpErrorCode.UnauthorizedAccess },
            { AmqpResponseStatusCode.Forbidden, AmqpErrorCode.ResourceLimitExceeded },
            { AmqpResponseStatusCode.Gone, AmqpErrorCode.Stolen.Value },
            { AmqpResponseStatusCode.InternalServerError, AmqpErrorCode.InternalError },
            { AmqpResponseStatusCode.BadRequest, ArgumentError },
            { AmqpResponseStatusCode.RequestTimeout, TimeoutError },
            { AmqpResponseStatusCode.ServiceUnavailable, ServerBusyError }
        };

        /// <summary>
        ///   Creates the exception that corresponds to a given AMQP response message.
        /// </summary>
        ///
        /// <param name="response">The response to consider.</param>
        /// <param name="eventHubsResource">The Event Hubs resource associated with the request.</param>
        ///
        /// <returns>The exception that most accurately represents the response failure.</returns>
        ///
        public static Exception CreateExceptionForResponse(AmqpMessage response,
                                                           string eventHubsResource)
        {
            if (response == null)
            {
                return new EventHubsException(true, eventHubsResource, Resources.UnknownCommunicationException);
            }

            if (!response.ApplicationProperties.Map.TryGetValue<string>(AmqpResponse.StatusDescription, out var description))
            {
                description = Resources.UnknownCommunicationException;
            }

            return CreateException(DetermineErrorCondition(response).Value, description, eventHubsResource);
        }

        /// <summary>
        ///   Creates the exception that corresponds to a given AMQP error.
        /// </summary>
        ///
        /// <param name="error">The AMQP error to consider.</param>
        /// <param name="eventHubsResource">The Event Hubs resource associated with the operation.</param>
        ///
        /// <returns>The exception that most accurately represents the error that was encountered.</returns>
        ///
        public static Exception CreateExceptionForError(Error error,
                                                        string eventHubsResource)
        {
            if (error == null)
            {
                return new EventHubsException(true, eventHubsResource, Resources.UnknownCommunicationException);
            }

            return CreateException(error.Condition.Value, error.Description, eventHubsResource);
        }

        /// <summary>
        ///   Creates the exception that corresponds to a given AMQP failure scenario.
        /// </summary>
        ///
        /// <param name="condition">The error condition that represents the failure scenario.</param>
        /// <param name="description">The descriptive text to use for messaging the scenario.</param>
        /// <param name="eventHubsResource">The Event Hubs resource associated with the failure.</param>
        ///
        /// <returns>The exception that most accurately represents the failure scenario.</returns>
        ///
        private static Exception CreateException(string condition,
                                                 string description,
                                                 string eventHubsResource)
        {
            // The request timed out.

            if (String.Equals(condition, TimeoutError.Value, StringComparison.InvariantCultureIgnoreCase))
            {
                return new EventHubsTimeoutException(eventHubsResource, description);
            }

            // The Event Hubs service was busy.

            if (String.Equals(condition, ServerBusyError.Value, StringComparison.InvariantCultureIgnoreCase))
            {
                return new ServiceBusyException(eventHubsResource, description);
            }

            // An argument was rejected by the Event Hubs service.

            if (String.Equals(condition, ArgumentError.Value, StringComparison.InvariantCultureIgnoreCase))
            {
                return new ArgumentException(description);
            }

            if (String.Equals(condition, ArgumentOutOfRangeError.Value, StringComparison.InvariantCultureIgnoreCase))
            {
                return new ArgumentOutOfRangeException(description);
            }

            // The consumer was superseded by one with a higher owner level.

            if (String.Equals(condition, AmqpErrorCode.Stolen.Value, StringComparison.InvariantCultureIgnoreCase))
            {
                return new ConsumerDisconnectedException(eventHubsResource, description);
            }

            // Authorization was denied.

            if (String.Equals(condition, AmqpErrorCode.UnauthorizedAccess.Value, StringComparison.InvariantCultureIgnoreCase))
            {
                return new UnauthorizedAccessException(description);
            }

            // Requests are being throttled due to exceeding the service quota.

            if (String.Equals(condition, AmqpErrorCode.ResourceLimitExceeded.Value, StringComparison.InvariantCultureIgnoreCase))
            {
                return new QuotaExceededException(eventHubsResource, description);
            }

            // The service does not understand how to process the request.

            if (String.Equals(condition, AmqpErrorCode.NotAllowed.Value, StringComparison.InvariantCultureIgnoreCase))
            {
                return new InvalidOperationException(description);
            }

            if (String.Equals(condition, AmqpErrorCode.NotImplemented.Value, StringComparison.InvariantCultureIgnoreCase))
            {
                return new NotSupportedException(description);
            }

            // The Event Hubs resource was not valid or communication with the service was interrupted.

            if (String.Equals(condition, AmqpErrorCode.NotFound.Value, StringComparison.InvariantCultureIgnoreCase))
            {
                if (NotFoundExpression.IsMatch(description)
                    || (description.IndexOf(NotFoundStatusText, StringComparison.InvariantCultureIgnoreCase) >= 0))
                {
                    return new EventHubsResourceNotFoundException(eventHubsResource, description);
                }


                return new EventHubsCommunicationException(eventHubsResource, description);
            }

            // There was no specific exception that could be determined; fall back to a generic one.

            return new EventHubsException(true, eventHubsResource, description);
        }

        /// <summary>
        ///   Determines the applicable error condition for a given response message.
        /// </summary>
        ///
        /// <param name="response">The AMQP response message to consider.</param>
        ///
        /// <returns>The AMQP error condition that best represents the response.</returns>
        ///
        private static AmqpSymbol DetermineErrorCondition(AmqpMessage response)
        {
            AmqpSymbol condition;

            // If there was an error condition present, use that.

            if (response.ApplicationProperties.Map.TryGetValue(AmqpResponse.ErrorCondition, out condition))
            {
                return condition;
            }

            // If no error condition was present, perform a reverse lookup in the mappings to determine the
            // condition from the response status code.

            if ((response.ApplicationProperties.Map.TryGetValue<int>(AmqpResponse.StatusCode, out var statusCode))
                && (StatusCodeMap.TryGetValue((AmqpResponseStatusCode)statusCode, out condition)))
            {
                return condition;
            }

            // If no specific value could be determined, fall back to a generic condition.

            return AmqpErrorCode.InternalError;
        }
    }
}
