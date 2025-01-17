﻿#nullable enable

using StateSmith.compiler.Visitors;
using StateSmith.Compiling;
using StateSmith.Input;
using System;
using System.Text;
using System.Linq;

namespace StateSmith.Runner
{
    public class ExceptionPrinter
    {
        public void PrintException(Exception exception, int depth = 0)
        {
            if (depth > 0)
            {
                Console.Error.WriteLine("==========================");
                Console.Error.WriteLine("Caused by below exception:");
            }

            string? customMessage = TryBuildingCustomExceptionDetails(exception);
            if (customMessage != null)
            {
                Console.Error.WriteLine(customMessage);
            }
            else
            {
                Console.Error.Write($"Exception {exception.GetType().Name} : ");
                Console.Error.WriteLine($"{exception.Message}");
            }

            if (exception.InnerException != null)
            {
                PrintException(exception.InnerException, depth + 1);
            }
        }

        public string OutputEdgeDetails(DiagramEdge edge)
        {
            StringBuilder sb = new StringBuilder();
            edge.Describe(sb);
            sb.Append("==========================\n");
            sb.Append("EDGE SOURCE NODE:\n");
            edge.source.Describe(sb);
            sb.Append("\n==========================\n");
            sb.Append("EDGE TARGET NODE:\n");
            edge.target.Describe(sb);

            return sb.ToString();
        }

        public string? TryBuildingCustomExceptionDetails(Exception ex)
        {
            switch (ex)
            {
                case DiagramEdgeParseException parseException:
                    {
                        DiagramEdge edge = parseException.edge;
                        string fromString = VertexPathDescriber.Describe(parseException.sourceVertex);
                        string toString = VertexPathDescriber.Describe(parseException.targetVertex);
                        string reasons = ex.Message.ReplaceLineEndings("\n           ");
                        string message = $@"Failed parsing diagram edge
from: {fromString}
to:   {toString}
Edge label: `{edge.label}`
Reason(s): {reasons}
Edge diagram id: {edge.id}
";
                        return message;
                    }

                case DiagramEdgeException diagramEdgeException:
                    {
                        string message = diagramEdgeException.Message;
                        message += OutputEdgeDetails(diagramEdgeException.edge);
                        return message;
                    }

                case VertexValidationException vertexValidationException:
                {
                    string message = nameof(VertexValidationException) + ": " + vertexValidationException.Message;

                    var vertex = vertexValidationException.vertex;
                    string fromString = VertexPathDescriber.Describe(vertex);

message += $@"
    Vertex
    Path: {fromString}
    Diagram Id: {vertex.DiagramId}
    Children count: {vertex.Children.Count()}
    Behaviors count: {vertex.Behaviors.Count()}
    Incoming transitions count: {vertex.IncomingTransitions.Count()}
";
                    return message;
                }


                case BehaviorValidationException behaviorValidationException:
                    {
                        string message = nameof(BehaviorValidationException) + ": " + behaviorValidationException.Message;

                        Behavior behavior = behaviorValidationException.behavior;
                        string fromString = VertexPathDescriber.Describe(behavior.OwningVertex);
                        string toString = VertexPathDescriber.Describe(behavior.TransitionTarget);

                        message += $@"
    Behavior
    Owning vertex: {fromString}
    Target vertex: {toString}
    Order: {behavior.GetOrderString()}
    Triggers: `{string.Join(", ", behavior.triggers)}`
    Guard: `{behavior.guardCode}`
    Action: `{behavior.actionCode}`
    Via Entry: `{behavior.viaEntry}`
    Via Exit : `{behavior.viaExit}`
";
                        return message;
                    }
            }

            return null;
        }
    }
}
