using UnityEngine;
using System.Collections.Generic;
using System;

/// <summary>
/// Evaluate a mathematical expression in string form.
/// </summary>
public class Calculator
{

	// Every operator, symbol, value and function is parsed into a Token
	public struct Token
	{
		public enum Type
		{
			OPERATOR_SIGN,
			OPERATOR_UNARY_PRE,
			OPERATOR_UNARY_POST,
			OPERATOR_BINARY_LEFT,
			OPERATOR_BINARY_RIGHT,
			OPERATOR_BOOLEAN,
			OPERATOR_TERNARY,
			STRING_DELIMITER,
			FUNCTION,
			ARGUMENT_SEPERATOR,
			TERNARY_SEPERATOR,
			OPEN_BRACKET,
			CLOSE_BRACKET,
			NUMERIC_VALUE,
			STRING_VALUE,
			BOOL_VALUE,
			NULL_VALUE,
			KEYWORD,
			ERROR
		}

		public delegate Token Evaluator(Token op, Token[] args);

		public Type m_Type { get; private set; }
		public string m_Value { get; private set; }
		public int m_Precedence { get; private set; }
		public Evaluator m_Evaluator { get; private set; }
		public int m_ArgCount { get; private set; }

		public float Numeric
		{
			get {
				float result;
				var good = float.TryParse(m_Value, out result);
				return good ? result : float.NaN;
			}
		}

		public bool Boolean
		{
			get {
				return (m_Value == Calculator.TRUE_VALUE);
			}
		}

		public override string ToString()
		{
			return m_Type.ToString() + ": \"" + m_Value + "\"";
		}

		/// <summary>
		/// Create a new expression token
		/// </summary>
		/// <param name="type">The type of token, e.g. Calculator.Token.Type.OPEN_BRACKET</param>
		/// <param name="value">The symbol as seen in the expression, e.g. "(", "45", "+"</param>
		/// <param name="precedence">Operator precedence, the higher the value the higher the precedence.</param>
		/// <param name="evaluator">A function to handle evaluation of the token.</param>
		/// <param name="argCount">Optional, number of arguments expected by the evaluator function.  If negative, then any number of parameters may be passed.</param>
		public Token(Type type, string value, int precedence, Evaluator evaluator, int argCount = 0)
		{
			m_Type = type;
			m_Value = value;
			m_Precedence = precedence;
			m_Evaluator = evaluator;
			m_ArgCount = argCount;
		}
	}

	public const string TRUE_VALUE = "true";
	public const string FALSE_VALUE = "false";

	/// <summary>
	/// Standard operator tokens supported by the calculator.
	/// </summary>
	static Token[] s_Operators = {
		new Token(Token.Type.OPERATOR_SIGN,         "+",         20, null),
		new Token(Token.Type.OPERATOR_SIGN,         "-",         20, null),
		new Token(Token.Type.OPERATOR_BINARY_LEFT,  "*",         40, EvaluateBinaryOperator),
		new Token(Token.Type.OPERATOR_BINARY_LEFT,  "/",         40, EvaluateBinaryOperator),
		new Token(Token.Type.OPERATOR_BINARY_LEFT,  "<",         10, EvaluateBinaryOperator),
		new Token(Token.Type.OPERATOR_BINARY_LEFT,  ">",         10, EvaluateBinaryOperator),
		new Token(Token.Type.OPERATOR_BINARY_LEFT,  "<=",        10, EvaluateBinaryOperator),
		new Token(Token.Type.OPERATOR_BINARY_LEFT,  ">=",        10, EvaluateBinaryOperator),
		new Token(Token.Type.OPERATOR_BINARY_LEFT,  "!=",        10, EvaluateBinaryOperator),
		new Token(Token.Type.OPERATOR_BINARY_LEFT,  "==",        10, EvaluateBinaryOperator),
		new Token(Token.Type.OPERATOR_BINARY_LEFT,  "&&",         0, EvaluateBooleanOperator),
		new Token(Token.Type.OPERATOR_BINARY_LEFT,  "||",         0, EvaluateBooleanOperator),
		BooleanToken(true),
		BooleanToken(false),
		new Token(Token.Type.OPEN_BRACKET,          "(",        100, null),
		new Token(Token.Type.CLOSE_BRACKET,         ")",        100, null),
		new Token(Token.Type.ARGUMENT_SEPERATOR,    ",",        -10, null),
		new Token(Token.Type.OPERATOR_TERNARY,      "?",        -10, null),
		new Token(Token.Type.TERNARY_SEPERATOR,     ":",        -10, null),
		new Token(Token.Type.STRING_DELIMITER,      "\"",         0, null),
		NullToken(),
		new Token(Token.Type.FUNCTION,              "floor",    100, EvaluateFunction, 1),
		new Token(Token.Type.FUNCTION,              "ceil",     100, EvaluateFunction, 1),
		new Token(Token.Type.FUNCTION,              "round",    100, EvaluateFunction, 1),
		new Token(Token.Type.FUNCTION,              "pow",      100, EvaluateFunction, 2),
		new Token(Token.Type.FUNCTION,              "abs",      100, EvaluateFunction, 1),
		new Token(Token.Type.FUNCTION,              "sin",      100, EvaluateFunction, 1),
		new Token(Token.Type.FUNCTION,              "cos",      100, EvaluateFunction, 1),
		new Token(Token.Type.FUNCTION,              "tan",      100, EvaluateFunction, 1),
		new Token(Token.Type.FUNCTION,              "atan2",    100, EvaluateFunction, 2),
		new Token(Token.Type.FUNCTION,              "atan",     100, EvaluateFunction, 1),
		new Token(Token.Type.KEYWORD,               "pi",       100, (f,a) => NumericToken(Mathf.PI))
	};

	/// <summary>
	/// Functionality of the calculator can be extended by adding Tokens with their own evaluation handlers here.
	/// By adding tokens here already supported by the Calculator, standard functionality may be overridden.
	/// </summary>
	public List<Token> m_ExtendedOperators = new List<Token>();

	/// The original expression, digested into Tokens
	List<Token> _infix;

	/// The expression in Reverse Polish Notation
	List<Token> _postfix;

	/// The result of evaluating the expression
	Token _result;

	//------------------------------------------------------
	//  Public API
	//------------------------------------------------------

	public string Evaluate(string expression)
	{
		return _evaluate(expression).m_Value;
	}

	public bool TryEvaluate(string expression, out string result)
	{
		var eval = _evaluate(expression);
		result = eval.m_Value;
		return eval.m_Type != Token.Type.ERROR;
	}

	public bool TryEvaluate(string expression, out float result)
	{
		var eval = _evaluate(expression);
		result = eval.Numeric;
		return eval.m_Type == Token.Type.NUMERIC_VALUE;
	}

	protected Token _evaluate(string expression)
	{
		Tokenize(expression);
		InfixToPostfix();
		EvaluatePostfix();

		return _result;
	}


	//------------------------------------------------------
	//  Step 1:  Tokenize the expression
	//------------------------------------------------------


	/** Parse the expression into an array of individual operators, operands and functions */
	void Tokenize(string expression)
	{
		var expr = expression.ToCharArray();

		//
		// Tokenize the string from left to right, adding tokens to _infix[]
		//
		_infix = new List<Token>();

		for (int c = 0; c < expr.Length; c++)
		{

			// Skip spaces
			if (expr[c] == ' ')
				continue;

			if (IsNumeric(expr[c]))
			{
				// NUMERIC VALUE FOUND
				// find characters that are part of this numeric value
				int i = c + 1;
				for (; i < expr.Length; i++)
				{
					if (!IsNumeric(expr[i])) break;
				}
				// push the value as a token, and remove from the expression
				_infix.Add(new Token(Token.Type.NUMERIC_VALUE, expression.Substring(c, i - c), 0, null));
				c = i - 1;
			}
			else
			{
				Token? op = FindNextOperator(expression, c, true);
				if (op.HasValue)
				{
					// FOUND AN OPERATOR OR EXPRESSION

					Token.Type? prevTokenType = null;
					if (_infix.Count > 0) prevTokenType = _infix[_infix.Count - 1].m_Type;

					switch (op.Value.m_Type)
					{

						case Token.Type.ARGUMENT_SEPERATOR:
							// Make sure parameters have at least a null value. ie,  func(,)  becomes:  func(null,null)
							if (prevTokenType == Token.Type.OPEN_BRACKET || prevTokenType == Token.Type.ARGUMENT_SEPERATOR)
							{
								_infix.Add(new Token(Token.Type.NULL_VALUE, "null", 0, null));
							}
							c += op.Value.m_Value.Length - 1;
							break;

						case Token.Type.CLOSE_BRACKET:
							// Make sure parameters have at least a null value. ie,  func(,)  becomes:  func(null,null)
							if (prevTokenType == Token.Type.ARGUMENT_SEPERATOR)
							{
								_infix.Add(new Token(Token.Type.NULL_VALUE, "null", 0, null));
							}
							c += op.Value.m_Value.Length - 1;
							break;

						case Token.Type.STRING_DELIMITER:
							// FOUND A STRING VALUE
							int end = expression.IndexOf(op.Value.m_Value, c + 1);
							op = new Token(Token.Type.STRING_VALUE, expression.Substring(c + 1, (end - c) - 1), 0, null);
							c = end;
							break;

						default:
							c += op.Value.m_Value.Length - 1;
							break;
					}

					// Add the token
					_infix.Add(op.Value);

				}
				else
				{

					// No values or expressions recognized?  Must be junk!
					_infix.Clear();
					_infix.Add(ErrorToken("Unrecognized characters in expression at index " + c));
					return;
				}
			}
		}
	}


	bool IsNumeric(char v)
	{
		return ((v >= '0' && v <= '9') || (v == '.'));
	}


	Token? FindNextOperator(string expression, int fromChar, bool findFunctions)
	{
		//TODO  This method will mistake similar function names { e.g. FooBar() mistakenly recognized as Foo() } causing unexpected results

		for (int index = 0; index < m_ExtendedOperators.Count; index++)
		{
			var op = m_ExtendedOperators[index];
			var symbol = op.m_Value;
			if (fromChar + symbol.Length <= expression.Length)
			{
				// Case-insensitive comparison with each symbol in the list
				if (string.Compare(expression.Substring(fromChar, symbol.Length), symbol, true) == 0)
				{
					if (op.m_Type != Token.Type.FUNCTION || findFunctions)
					{
						return op;
					}
				}
			}
		}

		for (int index = 0; index < s_Operators.Length; index++)
		{
			var op = s_Operators[index];
			var symbol = op.m_Value;
			if (fromChar + symbol.Length <= expression.Length)
			{
				// Case-insensitive comparison with each symbol in the list
				if (string.Compare(expression.Substring(fromChar, symbol.Length), symbol, true) == 0)
				{
					if (op.m_Type != Token.Type.FUNCTION || findFunctions)
					{
						return op;
					}
				}
			}
		}

		return null;
	}


	//--------------------------------------------------------------
	//  Step 2:  Convert the expression to Reverse Polish Notation
	//--------------------------------------------------------------

	void InfixToPostfix()
	{
		_postfix = new List<Token>();
		var stack = new Stack<Token>();

		Token? token = null;
		for (int i = 0; i < _infix.Count; i++)
		{

			Token.Type? prevType = null;
			if (token.HasValue)
				prevType = token.Value.m_Type;

			token = _infix[i];
			Token tokenA = token.Value;
			Token tokenB;

			switch (tokenA.m_Type)
			{

				case Token.Type.KEYWORD:
				case Token.Type.NUMERIC_VALUE:
				case Token.Type.BOOL_VALUE:
				case Token.Type.STRING_VALUE:
				case Token.Type.NULL_VALUE:
				case Token.Type.OPERATOR_UNARY_POST:
					// Append the token to the postfix output.
					_postfix.Add(tokenA);
					break;


				case Token.Type.FUNCTION:
					if (i < _infix.Count - 1 && _infix[i + 1].m_Type != Token.Type.OPEN_BRACKET)
					{
						_postfix.Clear();
						_postfix.Add(ErrorToken("Function missing open bracket: \"" + tokenA.m_Value));
					}
					else
					{
						// Push the token on to the stack.
						stack.Push(tokenA);
						// and add an open paren to postfix to mark where this function's parameters begin
						_postfix.Add(new Token(Token.Type.OPEN_BRACKET, "(", 100, null));
					}
					break;


				case Token.Type.OPERATOR_UNARY_PRE:
				case Token.Type.OPEN_BRACKET:
					// Push the token on to the stack.
					stack.Push(tokenA);
					break;


				case Token.Type.OPERATOR_BINARY_LEFT:
					while (stack.Count > 0)
					{
						tokenB = stack.Peek();
						if (tokenB.m_Precedence >= tokenA.m_Precedence && tokenB.m_Type != Token.Type.OPEN_BRACKET)
						{
							// Pop B off the stack and append it to the output
							_postfix.Add(stack.Pop());
						}
						else
						{
							break;
						}
					}
					// Push operator A onto the stack
					stack.Push(tokenA);
					break;


				case Token.Type.OPERATOR_BOOLEAN:
				case Token.Type.OPERATOR_BINARY_RIGHT:
					while (stack.Count > 0)
					{
						tokenB = stack.Peek();
						if (tokenB.m_Precedence > tokenA.m_Precedence && tokenB.m_Type != Token.Type.OPEN_BRACKET)
						{
							// Pop B off the stack and append it to the output
							_postfix.Add(stack.Pop());
						}
						else
						{
							break;
						}
					}
					// Push operator A onto the stack
					stack.Push(tokenA);
					break;


				case Token.Type.OPERATOR_SIGN:

					switch (prevType.Value)
					{
						case Token.Type.CLOSE_BRACKET:
						case Token.Type.NUMERIC_VALUE:
						case Token.Type.KEYWORD:
						case Token.Type.OPERATOR_UNARY_POST:
							// This is really a binary operator...
							while (stack.Count > 0)
							{
								tokenB = stack.Peek();
								if (tokenB.m_Precedence >= tokenA.m_Precedence && tokenB.m_Type != Token.Type.OPEN_BRACKET)
								{
									// Pop B off the stack and append it to the output
									_postfix.Add(stack.Pop());
								}
								else
								{
									break;
								}
							}
							// Change operator type and push it onto the stack
							tokenA = new Token(Token.Type.OPERATOR_BINARY_LEFT, tokenA.m_Value, tokenA.m_Precedence, EvaluateBinaryOperator);
							stack.Push(tokenA);
							break;

						default:
							// Token is an unary prefix operator.
							// Change operator type and push it onto the stack
							tokenA = new Token(Token.Type.OPERATOR_UNARY_PRE, tokenA.m_Value, tokenA.m_Precedence, EvaluateUnaryPre);
							stack.Push(tokenA);
							break;
					}
					break;


				case Token.Type.ARGUMENT_SEPERATOR:

					if (stack.Count == 0)
					{
						_postfix.Clear();
						_postfix.Add(ErrorToken("Missplaced argument seperator"));
						return;
					}

					do
					{
						tokenB = stack.Peek();
						if (tokenB.m_Type == Token.Type.OPEN_BRACKET || tokenB.m_Type == Token.Type.ARGUMENT_SEPERATOR)
							break;

						// Pop the top element from the stack and add to the output
						_postfix.Add(stack.Pop());
						// Repeat until the stack is empty, or we've come to an open bracket
					} while (stack.Count == 0);

					if (stack.Count == 0)
					{
						_postfix.Clear();
						_postfix.Add(ErrorToken("Missplaced argument seperator"));
						return;
					}

					stack.Push(tokenA);
					break;


				case Token.Type.CLOSE_BRACKET:
					if (stack.Count == 0)
					{
						_postfix.Clear();
						_postfix.Add(ErrorToken("Missmatched brackets"));
						return;
					}

					int argCount = 1;
					while (stack.Count > 0)
					{
						tokenB = stack.Peek();
						if (tokenB.m_Type == Token.Type.OPEN_BRACKET)
							break;

						// Pop the top element off the stack and add it to the output
						if (tokenB.m_Type == Token.Type.ARGUMENT_SEPERATOR)
						{
							// Throw away argument seperators, but keep track of how many parameters have been passed
							++argCount;
							stack.Pop();
						}
						else
						{
							_postfix.Add(stack.Pop());
						}

						// ...repeat until the top element of the stack is an open bracket
					}
					if (stack.Count == 0)
					{
						_postfix.Clear();
						_postfix.Add(ErrorToken("Missmatched brackets"));
						return;
					}
					// Pop the bracket off the stack
					stack.Pop();

					// If the token at the top of the stack is a function token
					if (stack.Count > 0)
					{
						tokenB = stack.Peek();
						if (tokenB.m_Type == Token.Type.FUNCTION)
						{
							// Check that the correct number of parameters were passed
							if (tokenB.m_ArgCount != argCount && tokenB.m_ArgCount >= 0)
							{
								_postfix.Clear();
								_postfix.Add(ErrorToken("Argument count mismatch.  Function " +
									tokenB.m_Value + "() expects " + tokenB.m_ArgCount + " parameter" + (tokenB.m_ArgCount == 1 ? "" : "s") +
									", but received " + argCount));
								return;
							}
							// Pop the top element off the stack and add it to the output
							_postfix.Add(stack.Pop());
						}
					}
					break;

				default:
					_postfix.Clear();
					_postfix.Add(ErrorToken("Unsupported symbol type: " + tokenA.m_Type));
					return;

			}
		}

		// Add the remaining stack to the output
		while (stack.Count > 0)
		{
			_postfix.Add(stack.Pop());
		}
	}


	//------------------------------------
	//  Step 3:  Evaluate the expression
	//------------------------------------

	void EvaluatePostfix()
	{
		if (_postfix.Count == 0)
		{
			_result = ErrorToken("Invalid expression");
			return;
		}

		//if (_postfix.Count == 1) {
		//	_result = _postfix[0];
		//	return;
		//}

		var stack = new Stack<Token>();
		Token[] args;

		// Evaluate each token in the postfix
		for (int i = 0; i < _postfix.Count; i++)
		{
			var token = _postfix[i];
			Token t;

			switch (token.m_Type)
			{

				case Token.Type.OPERATOR_UNARY_PRE:
				case Token.Type.OPERATOR_UNARY_POST:
					args = new Token[] { stack.Pop() };
					stack.Push(token.m_Evaluator(token, args));
					break;

				case Token.Type.OPERATOR_BINARY_LEFT:
				case Token.Type.OPERATOR_BINARY_RIGHT:
				case Token.Type.OPERATOR_BOOLEAN:
					t = stack.Pop();
					args = new Token[] { stack.Pop(), t };
					stack.Push(token.m_Evaluator(token, args));
					break;

				case Token.Type.FUNCTION:
					var argList = new List<Token>();
					while (stack.Count > 0)
					{
						t = stack.Pop();
						//  Open bracket denotes start of parameter list
						if (t.m_Type == Token.Type.OPEN_BRACKET)
							break;
						argList.Insert(0, t);
					}
					if (token.m_ArgCount >= 0 && token.m_ArgCount != argList.Count)
					{
						_result = ErrorToken("Argument count mismatch in Calculator function " + token.m_Value + "().  Expected " + token.m_ArgCount + " got " + argList.Count + ".");
						return;
					}
					stack.Push(token.m_Evaluator(token, argList.ToArray()));
					break;

				case Token.Type.KEYWORD:
					stack.Push(token.m_Evaluator(token, null));
					break;

				case Token.Type.STRING_VALUE:
				case Token.Type.BOOL_VALUE:
				case Token.Type.NUMERIC_VALUE:
				case Token.Type.NULL_VALUE:
				case Token.Type.OPEN_BRACKET:
					stack.Push(token);
					break;

				case Token.Type.ERROR:
					_result = token;
					return;
			}
		}

		// The stack should now contain only one value:  The result!
		if (stack.Count != 1)
		{
			_result = ErrorToken("Invalid expression");
		}
		_result = stack.Pop();
	}


	//------------------------------------------------------
	//  Standard evaluator methods
	//------------------------------------------------------


	static Token EvaluateFunction(Token func, Token[] args)
	{
		switch (func.m_Value)
		{
			case "floor":
				return NumericToken(Mathf.Floor(args[0].Numeric));
			case "ceil":
				return NumericToken(Mathf.Ceil(args[0].Numeric));
			case "round":
				return NumericToken(Mathf.Round(args[0].Numeric));
			case "sqrt":
				return NumericToken(Mathf.Sqrt(args[0].Numeric));
			case "abs":
				return NumericToken(Mathf.Abs(args[0].Numeric));
			case "pow":
				return NumericToken(Mathf.Pow(args[0].Numeric, args[1].Numeric));
			case "sin":
				return NumericToken(Mathf.Sin(args[0].Numeric));
			case "cos":
				return NumericToken(Mathf.Cos(args[0].Numeric));
			case "tan":
				return NumericToken(Mathf.Tan(args[0].Numeric));
			case "atan":
				return NumericToken(Mathf.Atan(args[0].Numeric));
			case "atan2":
				return NumericToken(Mathf.Atan2(args[0].Numeric, args[1].Numeric));
		}

		return ErrorToken("Unknown Function: \"" + func.m_Value + "\"");
	}


	static Token EvaluateBinaryOperator(Token op, Token[] args)
	{
		if (args[0].m_Type == Token.Type.BOOL_VALUE && args[1].m_Type == Token.Type.BOOL_VALUE)
		{
			var left = args[0].Boolean;
			var right = args[1].Boolean;

			switch (op.m_Value)
			{
				case "==":
					return BooleanToken(left == right);
				case "!=":
					return BooleanToken(left != right);
			}
		}
		else
		{
			var left = args[0].Numeric;
			var right = args[1].Numeric;
			if (float.IsNaN(left) || float.IsNaN(right))
				return NumericToken(float.NaN);

			switch (op.m_Value)
			{
				case "+":
					return NumericToken(left + right);
				case "-":
					return NumericToken(left - right);
				case "*":
					return NumericToken(left * right);
				case "/":
					return NumericToken(left / right);
				case "<":
					return BooleanToken(left < right);
				case ">":
					return BooleanToken(left > right);
				case "<=":
					return BooleanToken(left <= right);
				case ">=":
					return BooleanToken(left >= right);
				case "==":
					return BooleanToken(left == right);
				case "!=":
					return BooleanToken(left != right);
			}
		}

		return ErrorToken("Unknown Operator: \"" + op.m_Value + "\"");
	}


	static Token EvaluateBooleanOperator(Token op, Token[] args)
	{
		if (args[0].m_Type != Token.Type.BOOL_VALUE)
			return ErrorToken("Left operand is not a boolean value");
		if (args[1].m_Type != Token.Type.BOOL_VALUE)
			return ErrorToken("Right operand is not a boolean value");
		var left = args[0].Boolean;
		var right = args[1].Boolean;

		switch (op.m_Value)
		{
			case "&&":
				return BooleanToken(left && right);
			case "||":
				return BooleanToken(left || right);
		}

		return ErrorToken("Unknown Operator: \"" + op.m_Value + "\"");
	}


	static Token EvaluateUnaryPre(Token op, Token[] args)
	{
		var right = args[0].Numeric;

		switch (op.m_Value)
		{
			case "+":
				return args[0];
			case "-":
				return NumericToken(-right);
		}

		return ErrorToken("Unknown Operator: \"" + op.m_Value + "\"");
	}

	//---------------------------------------------
	//  Evaluation helper methods
	//---------------------------------------------

	static protected Token NumericToken(float value)
	{
		return new Token(Token.Type.NUMERIC_VALUE, value.ToString(), 0, null);
	}

	static protected Token ErrorToken(string message)
	{
		return new Token(Token.Type.ERROR, message, 0, null);
	}

	static protected Token BooleanToken(bool value)
	{
		return new Token(Token.Type.BOOL_VALUE, value ? TRUE_VALUE : FALSE_VALUE, 0, null);
	}

	static protected Token StringToken(string value)
	{
		return new Token(Token.Type.STRING_VALUE, value, 0, null);
	}

	static protected Token NullToken()
	{
		return new Token(Token.Type.NULL_VALUE, "null", 0, null);
	}

}

