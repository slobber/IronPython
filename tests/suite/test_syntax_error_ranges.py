import textwrap
import unittest

from iptest import run_test


class SyntaxErrorRangeTest(unittest.TestCase):
    def check(
        self,
        source,
        message,
        lineno,
        offset,
        end_lineno,
        end_offset,
    ):
        with self.assertRaisesRegex(SyntaxError, message):
            compile(source, "<test>", "exec")

        try:
            compile(source, "<test>", "exec")
        except SyntaxError as error:
            self.assertEqual(error.lineno, lineno)
            self.assertEqual(error.offset, offset)
            self.assertEqual(error.end_lineno, end_lineno)
            self.assertEqual(error.end_offset, end_offset)

    def test_unparenthesized_generator_expression(self):
        self.check(
            "foo(x,    y for y in range(3) for z in range(2) if z    , p)",
            "Generator expression must be parenthesized",
            1,
            11,
            1,
            53,
        )

    def test_mixed_except_forms(self):
        self.check(
            "try: pass\nexcept ValueError: pass\nexcept* TypeError: pass",
            r"cannot have both 'except' and 'except\*' on the same 'try'",
            3,
            1,
            3,
            8,
        )
        self.check(
            "try: pass\nexcept* ValueError: pass\nexcept TypeError: pass",
            r"cannot have both 'except' and 'except\*' on the same 'try'",
            3,
            1,
            3,
            7,
        )

    def test_except_attribute_target(self):
        self.check(
            textwrap.dedent(
                """
                try:
                    pass
                except ValueError as obj.attr:
                    pass
                """
            ),
            "cannot use except statement with attribute",
            4,
            22,
            4,
            30,
        )

    def test_match_attribute_target(self):
        self.check(
            textwrap.dedent(
                """
                match 1:
                    case x as obj.attr:
                        ...
                """
            ),
            "cannot use attribute as pattern target",
            3,
            15,
            3,
            23,
        )

    def test_operator_suggestions(self):
        self.check(
            "1<>2",
            r"Maybe you meant '!=' instead of '<>'\?",
            1,
            2,
            1,
            4,
        )
        self.check(
            "from __future__ import barry_as_FLUFL\na != b",
            "with Barry as BDFL, use '<>' instead of '!='",
            2,
            3,
            2,
            5,
        )


run_test(__name__)
