#nullable enable

using System;
using StateSmith.Compiling;
using StateSmith.Common;
using StateSmith.Input.antlr4;

namespace StateSmith.output.C99BalancedCoder1
{
    // FIXME test
    public class EventHandlerBuilder
    {
        private readonly CodeGenContext ctx;
        private readonly Statemachine sm;
        private readonly CNameMangler mangler;
        private readonly OutputFile file;

        public EventHandlerBuilder(CodeGenContext ctx, OutputFile file)
        {
            this.ctx = ctx;
            sm = ctx.sm;
            mangler = ctx.mangler;
            this.file = file;
        }

        // TODO refactor large method into smaller ones. The logic is a bit repetitive/unclear too.
        public void OutputStateBehaviorsForTrigger(NamedVertex state, string triggerName)
        {
            NamedVertex? nextHandlingState = null;
            bool noAncestorHandlesEvent;

            if (TriggerHelper.IsEvent(triggerName))
            {
                file.AddLine($"// setup handler for next ancestor that listens to `{triggerName}` event");
                file.Append("self->ancestor_event_handler = ");
                nextHandlingState = state.FirstAncestorThatHandlesEvent(triggerName);
                noAncestorHandlesEvent = nextHandlingState == null;
                if (nextHandlingState != null)
                {
                    file.FinishLine($"{mangler.SmFuncTriggerHandler(nextHandlingState, triggerName)};");
                }
                else
                {
                    file.FinishLine($"NULL; // no ancestor state handles `{triggerName}` event");
                }
            }
            noAncestorHandlesEvent = nextHandlingState == null;
    
            var behaviorsWithTrigger = TriggerHelper.GetBehaviorsWithTrigger(state, triggerName);
            foreach (var b in behaviorsWithTrigger)
            {
                bool requiredConsumeEventCode = TriggerHelper.IsEvent(triggerName);
                bool forceConsumeEvent = b.HasTransition(); // if has transition, event MUST be consumed. No variable option to override.
                bool hasConsumeEventVar = requiredConsumeEventCode && !forceConsumeEvent; // if has transition, event MUST be consumed. No variable option to override.

                file.AddLine();
                file.AddLine("// state behavior:");
                file.StartCodeBlockHere();
                {
                    if (forceConsumeEvent)
                    {
                        file.AddLine("// Note: no `consume_event` variable possible here because of state transition. The event must be consumed.");
                    }
                    else if (hasConsumeEventVar)
                    {
                        file.Append("bool consume_event = ");
                        if (TriggerHelper.IsDoEvent(triggerName))
                        {
                            file.FinishLine("false; // the `do` event is special in that it normally is not consumed.");
                        }
                        else
                        {
                            file.FinishLine("true; // events other than `do` are normally consumed by any event handler. Other event handlers in *this* state may still handle the event though.");
                        }
                        file.AddLine("(void)consume_event; // avoid un-used variable compiler warning. StateSmith cannot (yet) detect if behavior action code sets `consume_event`.");
                        
                        if (noAncestorHandlesEvent)
                        {
                            file.AddLine("// note: no ancestor consumes this event, but we output `bool consume_event` anyway because a user's design might rely on it.");
                        }
                        
                        file.AddLine();
                    }

                    DescribeBehaviorWithUmlComment(b);

                    StartGuardCodeIfNeeded(b);
                    OutputAnyActionCode(b);

                    OutputAnyTransitionCode(state, triggerName, b);

                    if (requiredConsumeEventCode)
                    {
                        file.AddLine();

                        if (forceConsumeEvent)
                        {
                            OutputConsumeEventCode(nextHandlingState);
                        }
                        else
                        {
                            // hasConsumeEventVar must be true
                            if (!hasConsumeEventVar)
                            {
                                throw new InvalidOperationException("This shouldn't happen");
                            }
                            file.Append("if (consume_event)");
                            file.StartCodeBlock();
                            {
                                OutputConsumeEventCode(nextHandlingState);
                            }
                            file.FinishCodeBlock();
                        }
                    }

                    if (b.HasTransition())
                    {
                        file.AddLine($"return; // event processing immediately stops when a transition occurs. No other behaviors for this state are checked.");
                    }

                    FinishGuardCodeIfNeeded(b);
                }
                file.FinishCodeBlock(" // end of behavior code");
            }
        }

        private void OutputConsumeEventCode(NamedVertex? nextHandlingState)
        {
            file.AddLine("// Mark event as handled. Required because of transition.");
            if (nextHandlingState != null)
            {
                file.AddLine("self->ancestor_event_handler = NULL;");
            }
            else
            {
                file.AddLine("// self->ancestor_event_handler = NULL; // already done at top of function");
            }
        }

        private void OutputAnyTransitionCode(NamedVertex state, string triggerName, Behavior b)
        {
            if (b.HasTransition() == false)
            {
                return;
            }

            if (b.HasActionCode())
            {
                file.AddLine();
            }

            ThrowIfHasTransitionOnEnterOrExitHandler(state, triggerName, b);

            NamedVertex target = (NamedVertex)b.TransitionTarget;   // will need to be updated when we allow transitioning to other types of vertices

            if (target != state)
            {
                OutputCodeForNonSelfTransition(state, target);
            }
            else
            {
                // self transition
                file.AddLine("// self transition");
                file.AddLine($"{mangler.SmFuncTriggerHandler(state, TriggerHelper.TRIGGER_EXIT)}(self);");
                file.AddLine($"{mangler.SmFuncTriggerHandler(state, TriggerHelper.TRIGGER_ENTER)}(self);");
            }
        }

        internal void OutputCodeForNonSelfTransition(NamedVertex state, NamedVertex target)
        {
            file.Append("// Transition to target state " + target.Name);
            file.StartCodeBlock();
            {
                var transitionPath = state.FindTransitionPathTo(target);
                if (transitionPath.leastCommonAncestor == state)
                {
                    file.AddLine($"// target state {target.Name} is a child of this state. No need to exit this state.");
                }
                else
                {
                    OutputPathExitToLcaCode(transitionPath);
                }

                file.AddLine();
                file.AddLine("// Enter towards target");
                foreach (var stateToEnter in transitionPath.toEnter)
                {
                    var enterHandler = mangler.SmFuncTriggerHandler((NamedVertex)stateToEnter, TriggerHelper.TRIGGER_ENTER);
                    file.AddLine($"{enterHandler}(self);");
                }

                file.AddLine();
                file.AddLine("// update state_id");
                file.AddLine($"self->state_id = {mangler.SmStateEnumValue(target)};");
            }
            file.FinishCodeBlock(" // end of transition code");
        }

        /// <summary>
        /// LCA means Least Common Ancestor
        /// </summary>
        /// <param name="transitionPath"></param>
        private void OutputPathExitToLcaCode(TransitionPath transitionPath)
        {
            NamedVertex leastCommonAncestor = ((NamedVertex)transitionPath.leastCommonAncestor);
            file.AddLine($"// First, exit up to Least Common Ancestor {leastCommonAncestor.Name}.");
            string lcaExitHandler = mangler.SmFuncTriggerHandler(leastCommonAncestor, TriggerHelper.TRIGGER_EXIT);
            file.Append($"while (self->current_state_exit_handler != {lcaExitHandler})");
            file.StartCodeBlock();
            {
                file.AddLine("self->current_state_exit_handler(self);");
            }
            file.FinishCodeBlock();
        }

        private void OutputAnyActionCode(Behavior b)
        {
            if (b.HasActionCode())
            {
                var expandedAction = ExpandingVisitor.ParseAndExpandCode(ctx.expander, b.actionCode);
                file.AddLines($"{expandedAction}");
            }
        }

        private void FinishGuardCodeIfNeeded(Behavior b)
        {
            if (b.HasGuardCode())
            {
                file.FinishCodeBlock(" // end of guard code");
            }
        }

        private void StartGuardCodeIfNeeded(Behavior b)
        {
            if (b.HasGuardCode())
            {
                var expandedGuardCode = ExpandingVisitor.ParseAndExpandCode(ctx.expander, b.guardCode);
                file.Append($"if ({expandedGuardCode})");
                file.StartCodeBlock();
            }
        }

        private static void ThrowIfHasTransitionOnEnterOrExitHandler(NamedVertex state, string triggerName, Behavior b)
        {
            if (TriggerHelper.IsEnterExitTrigger(triggerName))
            {
                if (b.TransitionTarget != null)
                {
                    throw new VertexValidationException(state, "Enter behaviors can not transition to another state");
                }
            }
        }

        private void DescribeBehaviorWithUmlComment(Behavior b)
        {
            if (b.HasGuardCode())
            {
                string sanitizedGuardCode = StringUtils.ReplaceNewLineChars(b.guardCode, "\n//            ");
                file.AddLines($"// uml guard: {sanitizedGuardCode}");
            }

            if (b.HasActionCode())
            {
                string sanitized = StringUtils.ReplaceNewLineChars(b.actionCode.Trim(), "\n//             ");
                file.AddLines($"// uml action: {sanitized}");
            }

            if (b.TransitionTarget != null)
            {
                NamedVertex target = (NamedVertex)b.TransitionTarget;   // will need to be updated when we allow transitioning to other types of vertices
                file.AddLine($"// uml transition target: {target.Name}");
            }
        }
    }
}