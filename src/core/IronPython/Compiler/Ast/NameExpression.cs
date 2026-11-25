// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using MSAst = System.Linq.Expressions;

using Microsoft.Scripting;

using IronPython.Runtime.Binding;

using AstUtils = Microsoft.Scripting.Ast.Utils;

namespace IronPython.Compiler.Ast {
    using Ast = MSAst.Expression;

    public class NameExpression : Expression {
        public NameExpression(string name) {
            Name = name;
        }

        public string Name { get; }

        /// <summary>
        /// True when this name is a compiler temporary rather than a name
        /// visible in Python source. Such names participate in code generation
        /// but are omitted from locals(), co_varnames, and closure dictionaries.
        /// </summary>
        internal bool IsCompilerGenerated { get; set; }

        internal bool SuppressDebugInfo { get; set; }

        internal PythonReference Reference { get; set; }

        /// <summary>
        /// definitely assigned
        /// </summary>
        internal bool Assigned { get; set; }

        public override string ToString() => base.ToString() + ":" + Name;

        public override MSAst.Expression Reduce() {
            MSAst.Expression read;

            if (Name is "__build_type_alias__" or "__build_generic_base__" or "__build_type_parameter__") {
                read = Ast.Call(
                    typeof(IronPython.Runtime.Operations.PythonOps).GetMethod(
                        nameof(IronPython.Runtime.Operations.PythonOps.GetCompilerIntrinsic))!,
                    Parent.LocalContext,
                    Ast.Constant(Name));
            } else if (Reference.PythonVariable == null) {
                read = Ast.Call(
                    AstMethods.LookupName,
                    Parent.LocalContext,
                    Ast.Constant(Name)
                );
            } else {
                read = Parent.LookupVariableExpression(Reference.PythonVariable);
            }

            if (!Assigned && !(read is IPythonGlobalExpression)) {
                read = Ast.Call(
                    Parent.IsFreeVariable(Reference.PythonVariable) ?
                        AstMethods.CheckUninitializedFree :
                        AstMethods.CheckUninitializedLocal,
                    read,
                    Ast.Constant(Name)
                );
            }

            return read;
        }

        internal override MSAst.Expression TransformSet(SourceSpan span, MSAst.Expression right, PythonOperationKind op) {
            MSAst.Expression assignment;

            if (op != PythonOperationKind.None) {
                right = GlobalParent.Operation(
                    typeof(object),
                    op,
                    this,
                    right
                );
            }

            SourceSpan aspan = span.IsValid && !SuppressDebugInfo
                ? new SourceSpan(Span.Start, span.End)
                : SourceSpan.None;

            if (Reference.PythonVariable != null) {
                assignment = AssignValue(
                    Parent.GetVariableExpression(Reference.PythonVariable),
                    ConvertIfNeeded(right, typeof(object))
                );
                if (Parent is ClassDefinition classDefinition
                    && classDefinition.IsExposedLocal(Reference.PythonVariable)) {
                    assignment = Ast.Block(
                        assignment,
                        Ast.Call(
                            null,
                            AstMethods.SetName,
                            Parent.LocalContext,
                            Ast.Constant(Name),
                            AstUtils.Convert(right, typeof(object))));
                }
            } else {
                assignment = Ast.Call(
                    null,
                    AstMethods.SetName,
                    Parent.LocalContext,
                    Ast.Constant(Name),
                    AstUtils.Convert(right, typeof(object))
                );
            }

            return GlobalParent.AddDebugInfoAndVoid(assignment, aspan);
        }

        internal override string CheckAssign() => null;

        internal override string CheckDelete() => null;

        internal override MSAst.Expression TransformDelete() {
            if (Reference.PythonVariable != null) {
                MSAst.Expression variable = Parent.GetVariableExpression(Reference.PythonVariable);
                // keep the variable alive until we hit the del statement to
                // better match CPython's lifetimes
                MSAst.Expression del = Ast.Block(
                    Ast.Call(
                        AstMethods.KeepAlive,
                        variable
                    ),
                    Delete(variable)
                );

                if (!Assigned) {
                    del = Ast.Block(
                        this,
                        del,
                        AstUtils.Empty()
                    );
                }
                return del;
            } else {
                return Ast.Call(
                    AstMethods.RemoveName,
                    Parent.LocalContext,
                    Ast.Constant(Name)
                );
            }
        }

        public override string NodeName => "name";

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
            }
            walker.PostWalk(this);
        }

        internal override bool CanThrow => !Assigned;
    }
}
