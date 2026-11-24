import io
import json
import tokenize


CASES = [
    ("indentation", "if x:\n    y = 1\n"),
    ("soft_keywords", "match case type _ while\n"),
    ("fstring", "f\"x={value}\"\n"),
    ("tstring", "t\"hello {name}\"\n"),
    ("nested_fstring", "f\"{prefix + f'{value}'}\"\n"),
    ("utf8_columns", "\u00e9 = 1\n"),
]


def tokenize_case(source):
    result = []
    for token in tokenize.generate_tokens(io.StringIO(source).readline):
        result.append(
            {
                "kind": tokenize.tok_name[token.type],
                "text": token.string,
                "start": [token.start[0], token.start[1]],
                "end": [token.end[0], token.end[1]],
            }
        )
    return result


print(
    json.dumps(
        [
            {"name": name, "source": source, "tokens": tokenize_case(source)}
            for name, source in CASES
        ],
        ensure_ascii=False,
        indent=2,
    )
)
