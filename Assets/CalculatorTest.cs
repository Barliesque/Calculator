using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System;

public class CalculatorTest : MonoBehaviour
{

	public InputField m_InputField;
	public Button m_SubmitButton;
	public Text m_Results;

	Calculator _calculator;


	void Start()
	{
		_calculator = new Calculator();

		// Example custom constant:  "favorite" returns my favorite number, 7
		var customConstant = new Calculator.Token(Calculator.Token.Type.KEYWORD, "favorite", 100, (f, a) => Calculator.NumericToken(7f));
		_calculator.m_ExtendedOperators.Add(customConstant);

		// Example custom function:  "smallest()" returns whichever value is closest to zero
		var customFunc = new Calculator.Token(Calculator.Token.Type.FUNCTION, "smallest", 100, Smallest, 2);
		_calculator.m_ExtendedOperators.Add(customFunc);

		m_SubmitButton.onClick.AddListener(Evaluate);
	}


	/// <summary>
	/// A custom Calculator function to find the smallest of two values
	/// </summary>
	/// <param name="op">The operator token, which will always be "smallest" function token</param>
	/// <param name="args">The parameter tokens</param>
	/// <returns>The numeric result of the function</returns>
	private Calculator.Token Smallest(Calculator.Token op, Calculator.Token[] args)
	{
		if (args.Length != 2)
		{
			return Calculator.ErrorToken("Smallest() requires two parameters");
		}
		if (args[0].m_Type != Calculator.Token.Type.NUMERIC_VALUE)
		{
			return Calculator.ErrorToken("Smallest() requires numeric parameters");
		}
		if (args[1].m_Type != Calculator.Token.Type.NUMERIC_VALUE)
		{
			return Calculator.ErrorToken("Smallest() requires numeric parameters");
		}
		if (Mathf.Abs(args[0].Numeric) < Mathf.Abs(args[1].Numeric))
		{
			return args[0];
		}
		else
		{
			return args[1];
		}
	}


	void Evaluate()
	{
		m_Results.text = _calculator.Evaluate(m_InputField.text);
	}


}
