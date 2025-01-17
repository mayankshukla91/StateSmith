using StateSmith.compiler.Visitors;

namespace StateSmith.Compiling
{
    public class ExitPoint : Vertex
    {
        public string label;

        public ExitPoint(string label)
        {
            this.label = label;
        }

        public override void Accept(VertexVisitor visitor)
        {
            visitor.Visit(this);
        }
    }
}
