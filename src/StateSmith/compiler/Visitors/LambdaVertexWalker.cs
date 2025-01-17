﻿using StateSmith.Compiling;
using System;

namespace StateSmith.compiler.Visitors
{
    public class LambdaVertexWalker : VertexWalker
    {
        public Action<Vertex> enterAction;
        public Action<Vertex> exitAction;

        public override void VertexEntered(Vertex v)
        {
            enterAction(v);
        }
        public override void VertexExited(Vertex v)
        {
            exitAction?.Invoke(v);
        }
    }
}
