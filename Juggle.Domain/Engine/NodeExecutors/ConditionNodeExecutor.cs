namespace Juggle.Domain.Engine.NodeExecutors;

/// <summary>
/// CONDITION 节点执行器：按条件表达式选择下一个分支。
/// 表达式语法（见 ExpressionEvaluator）：
///   逻辑 && || ! 括号；比较 == != > < >= <=；算术 + - * / %
///   左右两边都可以是变量/字面量
///   子属性(env_user.name)、数组长度(input_list.length)、数组元素(input_list[0].name)
///   字符串拼接(+) 切片([..5]/[2..5]) 替换(replace) 格式化(toString)
/// </summary>
public class ConditionNodeExecutor : INodeExecutor
{
    public Task<string?> ExecuteAsync(FlowNode node, FlowContext context)
    {
        if (node.Conditions == null || node.Conditions.Count == 0)
            return Task.FromResult<string?>(null);

        string? defaultOutgoing = null;

        foreach (var condition in node.Conditions)
        {
            if (condition.ConditionType == "DEFAULT")
            {
                defaultOutgoing = condition.Outgoing;
                continue;
            }

            // 计算表达式
            if (!string.IsNullOrWhiteSpace(condition.Expression))
            {
                var result = EvaluateExpression(condition.Expression, context);
                if (result)
                    return Task.FromResult(condition.Outgoing);
            }
        }

        // 没有匹配，走 DEFAULT
        return Task.FromResult(defaultOutgoing);
    }

    private static bool EvaluateExpression(string expression, FlowContext context)
        => ExpressionEvaluator.Evaluate(expression, context);
}
