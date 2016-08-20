﻿/*
 *	Firebird ADO.NET Data provider for .NET and Mono
 *
 *	   The contents of this file are subject to the Initial
 *	   Developer's Public License Version 1.0 (the "License");
 *	   you may not use this file except in compliance with the
 *	   License. You may obtain a copy of the License at
 *	   http://www.firebirdsql.org/index.php?op=doc&id=idpl
 *
 *	   Software distributed under the License is distributed on
 *	   an "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, either
 *	   express or implied. See the License for the specific
 *	   language governing rights and limitations under the License.
 *
 *	Copyright (c) 2002, 2007 Carlos Guzman Alvarez
 *	All Rights Reserved.
 *
 * Contributors:
 *   Jiri Cincura (jiri@cincura.net)
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Reflection;
using System.Resources;
using System.Runtime.Serialization;

namespace FirebirdSql.Data.Common
{
	[Serializable]
	internal sealed class IscException : Exception
	{
		#region Fields
		private string _message;
		#endregion

		#region Properties

		public List<IscError> Errors { get; private set; }

		public override string Message
		{
			get { return _message; }
		}

		public int ErrorCode { get; private set; }

		public string SQLSTATE { get; private set; }

		public bool IsWarning
		{
			get
			{
				if (Errors.Count > 0)
				{
					return Errors[0].IsWarning;
				}
				else
				{
					return false;
				}
			}
		}

		#endregion

		#region Constructors

		private IscException(Exception innerException = null)
			: base(innerException?.Message, innerException)
		{
			Errors = new List<IscError>();
		}

		public static IscException ForBuilding()
		{
			return new IscException();
		}

		public static IscException ForErrorCode(int errorCode, Exception innerException = null)
		{
			var result = new IscException(innerException);
			result.Errors.Add(new IscError(IscCodes.isc_arg_gds, errorCode));
			result.BuildExceptionData();
			return result;
		}

		public static IscException ForErrorCodes(IEnumerable<int> errorCodes, Exception innerException = null)
		{
			var result = new IscException(innerException);
			foreach (int errorCode in errorCodes)
			{
				result.Errors.Add(new IscError(IscCodes.isc_arg_gds, errorCode));
			}
			result.BuildExceptionData();
			return result;
		}

		public static IscException ForSQLSTATE(string sqlState, Exception innerException = null)
		{
			var result = new IscException(innerException);
			result.Errors.Add(new IscError(IscCodes.isc_arg_sql_state, sqlState));
			result.BuildExceptionData();
			return result;
		}

		public static IscException ForStrParam(string strParam, Exception innerException = null)
		{
			var result = new IscException(innerException);
			result.Errors.Add(new IscError(IscCodes.isc_arg_string, strParam));
			result.BuildExceptionData();
			return result;
		}

		public static IscException ForErrorCodeIntParam(int errorCode, int intParam, Exception innerException = null)
		{
			var result = new IscException(innerException);
			result.Errors.Add(new IscError(IscCodes.isc_arg_gds, errorCode));
			result.Errors.Add(new IscError(IscCodes.isc_arg_number, intParam));
			result.BuildExceptionData();
			return result;
		}

		public static IscException ForTypeErrorCodeStrParam(int type, int errorCode, string strParam, Exception innerException = null)
		{
			var result = new IscException(innerException);
			result.Errors.Add(new IscError(type, errorCode));
			result.Errors.Add(new IscError(IscCodes.isc_arg_string, strParam));
			result.BuildExceptionData();
			return result;
		}

		public static IscException ForTypeErrorCodeIntParamStrParam(int type, int errorCode, int intParam, string strParam, Exception innerException = null)
		{
			var result = new IscException(innerException);
			result.Errors.Add(new IscError(type, errorCode));
			result.Errors.Add(new IscError(IscCodes.isc_arg_number, intParam));
			result.Errors.Add(new IscError(IscCodes.isc_arg_string, strParam));
			result.BuildExceptionData();
			return result;
		}

		private IscException(SerializationInfo info, StreamingContext context)
				: base(info, context)
		{
			Errors = (List<IscError>)info.GetValue("errors", typeof(List<IscError>));
			ErrorCode = info.GetInt32("errorCode");
		}

		#endregion

		#region Public Methods

		public void BuildExceptionData()
		{
			BuildErrorCode();
			BuildSqlState();
			BuildExceptionMessage();
		}

		public override void GetObjectData(SerializationInfo info, StreamingContext context)
		{
			base.GetObjectData(info, context);

			info.AddValue("errors", Errors);
			info.AddValue("errorCode", ErrorCode);
		}

		public override string ToString()
		{
			return _message;
		}

		#endregion

		#region Private Methods

		private void BuildErrorCode()
		{
			ErrorCode = (Errors.Count != 0 ? Errors[0].ErrorCode : 0);
		}

		private void BuildSqlState()
		{
			IscError error = Errors.Find(e => e.Type == IscCodes.isc_arg_sql_state);
			// step #1, maybe we already have a SQLSTATE stuffed in the status vector
			if (error != null)
			{
				SQLSTATE = error.StrParam;
			}
			// step #2, see if we can find a mapping.
			else
			{
				SQLSTATE = GetValueOrDefault(SqlStateMapping.Values, ErrorCode, _ => string.Empty);
			}
		}

		private void BuildExceptionMessage()
		{
			StringBuilder builder = new StringBuilder();

			for (int i = 0; i < Errors.Count; i++)
			{
				if (Errors[i].Type == IscCodes.isc_arg_gds || Errors[i].Type == IscCodes.isc_arg_warning)
				{
					int code = Errors[i].ErrorCode;
					string message = GetValueOrDefault(IscErrorMessages.Values, code, BuildDefaultErrorMessage);

					List<string> args = new List<string>();
					int index = i + 1;
					while (index < Errors.Count && Errors[index].IsArgument)
					{
						args.Add(Errors[index++].StrParam);
						i++;
					}

					try
					{
						switch (code)
						{
							case IscCodes.isc_except:
								// Custom exception	add	the	first argument as error	code
								ErrorCode = Convert.ToInt32(args[0], CultureInfo.InvariantCulture);
								// ignoring the message - historical reason
								break;
							case IscCodes.isc_except2:
								// Custom exception. Next Error should be the exception name.
								// And the next one the Exception message
								break;
							case IscCodes.isc_stack_trace:
								// The next error contains the PSQL Stack Trace
								AppendMessage(builder, message, args);
								break;
							default:
								AppendMessage(builder, message, args);
								break;
						}
					}
					catch
					{
						message = BuildDefaultErrorMessage(code);
						AppendMessage(builder, message, args);
					}
				}
			}

			// Update error	collection only	with the main error
			IscError mainError = new IscError(ErrorCode);
			mainError.Message = builder.ToString();

			Errors.Add(mainError);

			// Update exception	message
			_message = builder.ToString();
		}

		private string BuildDefaultErrorMessage(int code)
		{
			return string.Format(CultureInfo.CurrentCulture, "No message for error code {0} found.", code);
		}

		#endregion

		#region Static Methods

		private static string GetValueOrDefault(IDictionary<int, string> dictionary, int key, Func<int, string> defaultValueFactory)
		{
			string result;
			if (!dictionary.TryGetValue(key, out result))
			{
				result = defaultValueFactory(key);
			}
			return result;
		}

		private static void AppendMessage(StringBuilder builder, string message, List<string> args)
		{
			if (builder.Length > 0)
			{
				builder.Append(Environment.NewLine);
			}
			builder.AppendFormat(CultureInfo.CurrentCulture, message, args.ToArray());
		}

		#endregion
	}
}