using System;
using System.Collections.Generic;

namespace IronPython.ParserGenerator {
    public sealed class AsdlModule {
        public AsdlModule(string name, IReadOnlyList<AsdlTypeDefinition> types) {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Types = types ?? throw new ArgumentNullException(nameof(types));
        }

        public string Name { get; }
        public IReadOnlyList<AsdlTypeDefinition> Types { get; }
    }

    public sealed class AsdlTypeDefinition {
        public AsdlTypeDefinition(
            string name,
            bool isProduct,
            IReadOnlyList<AsdlField> productFields,
            IReadOnlyList<AsdlConstructor> constructors,
            IReadOnlyList<AsdlField> attributes) {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            IsProduct = isProduct;
            ProductFields = productFields ?? throw new ArgumentNullException(nameof(productFields));
            Constructors = constructors ?? throw new ArgumentNullException(nameof(constructors));
            Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
        }

        public string Name { get; }
        public bool IsProduct { get; }
        public IReadOnlyList<AsdlField> ProductFields { get; }
        public IReadOnlyList<AsdlConstructor> Constructors { get; }
        public IReadOnlyList<AsdlField> Attributes { get; }
    }

    public sealed class AsdlConstructor {
        public AsdlConstructor(string name, IReadOnlyList<AsdlField> fields) {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Fields = fields ?? throw new ArgumentNullException(nameof(fields));
        }

        public string Name { get; }
        public IReadOnlyList<AsdlField> Fields { get; }
    }

    public sealed class AsdlField {
        public AsdlField(string typeName, string name, bool optional, bool sequence) {
            TypeName = typeName ?? throw new ArgumentNullException(nameof(typeName));
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Optional = optional;
            Sequence = sequence;
        }

        public string TypeName { get; }
        public string Name { get; }
        public bool Optional { get; }
        public bool Sequence { get; }
    }
}
