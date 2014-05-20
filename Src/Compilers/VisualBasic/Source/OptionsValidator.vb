﻿' Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

Imports System.Collections.Generic
Imports System.Runtime.InteropServices
Imports Microsoft.CodeAnalysis.Text
Imports Microsoft.CodeAnalysis.VisualBasic.Symbols
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax
Imports TypeKind = Microsoft.CodeAnalysis.TypeKind

Namespace Microsoft.CodeAnalysis.VisualBasic
    ''' <summary>
    ''' This class is used to validate the compiler options.
    ''' </summary>
    Friend Module OptionsValidator
        ''' <summary>
        ''' Parse the project level imports, adding errors to the errorBag as necessary
        ''' </summary>
        Friend Function ParseImports(importsClauses As IEnumerable(Of String), diagnostics As DiagnosticBag) As GlobalImport()
            Debug.Assert(diagnostics IsNot Nothing)

            Dim importsClauseArray = importsClauses.Select(Function(name) VisualBasicCommandLineParser.Unquote(name)).ToArray()

            If importsClauseArray.Length > 0 Then
                ' Create a file with Import statement for each imported name, and parse it. Use two newlines
                ' after each to avoid issues with implicit line continuation.
                Dim importFileText As String = importsClauseArray.Select(Function(name) "Imports " + name + vbCrLf + vbCrLf).Aggregate(Function(a, b) a & b)
                Dim tree = VisualBasicSyntaxTree.ParseText(SourceText.From(importFileText), VisualBasicParseOptions.Default, "")

                ' Extract all the parsed imports back out.
                Dim parsedImportList As New List(Of GlobalImport)
                Dim importList As SyntaxList(Of ImportsStatementSyntax) = tree.GetCompilationUnitRoot().Imports
                For i = 0 To importList.Count - 1
                    Dim statement = importList(i)
                    Dim importsClausesSyntaxList As SeparatedSyntaxList(Of ImportsClauseSyntax) = statement.ImportsClauses

                    If importsClauses.Count > 0 Then
                        Dim clause As ImportsClauseSyntax = importsClausesSyntaxList(0)
                        Dim syntaxErrors As IEnumerable(Of Diagnostic) = clause.GetSyntaxErrors(tree)

                        If importsClausesSyntaxList.Count > 1 Then
                            ' Only allow one import clause per name. If more than one is found, report "expected end of statement".
                            syntaxErrors = syntaxErrors.Concat(New VBDiagnostic(New DiagnosticInfo(MessageProvider.Instance, ERRID.ERR_ExpectedEOS), importsClausesSyntaxList(1).GetLocation()))
                        End If

                        Dim import = New GlobalImport(clause, importsClauseArray(i))

                        Dim errors = From diag In syntaxErrors
                                     Select import.MapDiagnostic(diag)

                        diagnostics.AddRange(errors)

                        If Not errors.Any(Function(diag) diag.Severity = DiagnosticSeverity.Error) Then
                            ' only add imports without syntax errors.
                            parsedImportList.Add(import)
                        End If

                    End If
                Next

                Return parsedImportList.ToArray()
            Else
                Return New GlobalImport() {}
            End If
        End Function

        ''' <summary>
        ''' Validate and parse the root namespace. If the root namespace string is a valid namespace name, the parsed
        ''' version is returned. Otherwise Nothing is returned.
        ''' </summary>
        Friend Function IsValidNamespaceName(name As String) As Boolean
            Debug.Assert(name IsNot Nothing)

            ' TODO: consider calling Syntax.ParseName and analyze the result (reject () or 
            ' type characters instead of reimplementing a lot of logic in IsValidRootNamespaceComponent.

            For Each nsName In name.Split("."c)
                If Not IsValidRootNamespaceComponent(nsName, allowEscaping:=True) Then
                    ' ERRID.ERR_BadNamespaceName1
                    Return False
                End If
            Next

            Return True
        End Function

        ''' <summary>
        ''' Check if a string is a valid component of the root namespace. We use the usual
        ''' VB identifier rules, but don't check for keywords (this is the same as previous versions).
        ''' </summary>
        Private Function IsValidRootNamespaceComponent(name As String, allowEscaping As Boolean) As Boolean
            Debug.Assert(name IsNot Nothing)

            Dim nameLength = name.Length
            ' Empty string is not valid.
            If nameLength = 0 Then
                Return False
            End If

            Dim firstIdentifierCharacterIndex As Integer = 0
            Dim lastIdentifierCharacterIndex As Integer = nameLength - 1

            If SyntaxFacts.ReturnFullWidthOrSelf(name(0)) = SyntaxFacts.FULLWIDTH_LBR AndAlso allowEscaping Then
                If SyntaxFacts.ReturnFullWidthOrSelf(name(lastIdentifierCharacterIndex)) <> SyntaxFacts.FULLWIDTH_RBR Then
                    Return False
                End If

                Return IsValidRootNamespaceComponent(name.Substring(1, nameLength - 2), allowEscaping:=False)
            End If

            If Not SyntaxFacts.IsIdentifierStartCharacter(name(firstIdentifierCharacterIndex)) Then
                Return False
            End If

            ' an identifier starting with an underscore must at least consist of two characters
            If SyntaxFacts.ReturnFullWidthOrSelf(name(firstIdentifierCharacterIndex)) = SyntaxFacts.FULLWIDTH_LC AndAlso nameLength = 1 Then
                Return False
            End If

            For i = firstIdentifierCharacterIndex + 1 To lastIdentifierCharacterIndex
                If Not SyntaxFacts.IsIdentifierPartCharacter(name(i)) Then
                    Return False
                End If
            Next

            Return True
        End Function
    End Module
End Namespace
