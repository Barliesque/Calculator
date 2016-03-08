using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System;

public class CalculatorTest : MonoBehaviour {

	public InputField	m_InputField;
	public Button		m_SubmitButton;
	public Text			m_Results;

	Calculator _calculator;

	// Use this for initialization
	void Start () {
		_calculator = new Calculator();
		m_SubmitButton.onClick.AddListener(Evaluate);
	}

	void Evaluate()
	{
		m_Results.text = _calculator.Evaluate(m_InputField.text);
	}


}
