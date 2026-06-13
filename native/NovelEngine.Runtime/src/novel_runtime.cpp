#include "novel_runtime.h"

#include <algorithm>
#include <charconv>
#include <cctype>
#include <cmath>
#include <memory>
#include <regex>
#include <sstream>
#include <stdexcept>
#include <string>
#include <string_view>
#include <system_error>
#include <unordered_map>
#include <variant>

namespace {

using value = std::variant<std::monostate, bool, double, std::string>;

struct runtime_state {
    std::unordered_map<std::string, value> variables;
    std::string last_error;
    std::string string_buffer;
};

std::string trim(std::string_view source) {
    const auto first = std::find_if_not(
        source.begin(),
        source.end(),
        [](unsigned char character) { return std::isspace(character) != 0; });
    const auto last = std::find_if_not(
        source.rbegin(),
        source.rend(),
        [](unsigned char character) { return std::isspace(character) != 0; }).base();
    return first < last ? std::string(first, last) : std::string();
}

value parse_literal(std::string source) {
    source = trim(source);
    if (source.size() >= 2 && source.front() == '"' && source.back() == '"') {
        std::string result;
        result.reserve(source.size() - 2);
        for (std::size_t index = 1; index + 1 < source.size(); ++index) {
            if (source[index] == '\\' && index + 2 < source.size()) {
                const char escaped = source[++index];
                result.push_back(escaped == 'n' ? '\n' : escaped);
            } else {
                result.push_back(source[index]);
            }
        }
        return result;
    }

    std::string lowered = source;
    std::transform(
        lowered.begin(),
        lowered.end(),
        lowered.begin(),
        [](unsigned char character) { return static_cast<char>(std::tolower(character)); });
    if (lowered == "true") {
        return true;
    }
    if (lowered == "false") {
        return false;
    }
    if (lowered == "null") {
        return std::monostate{};
    }

    double number = 0;
    const auto parsed = std::from_chars(
        source.data(),
        source.data() + source.size(),
        number);
    if (parsed.ec == std::errc{} && parsed.ptr == source.data() + source.size()) {
        return number;
    }
    return source;
}

bool as_number(const value& source, double& result) {
    if (const auto* number = std::get_if<double>(&source)) {
        result = *number;
        return true;
    }
    return false;
}

std::string as_string(const value& source) {
    if (const auto* text = std::get_if<std::string>(&source)) {
        return *text;
    }
    if (const auto* number = std::get_if<double>(&source)) {
        return std::to_string(*number);
    }
    if (const auto* boolean = std::get_if<bool>(&source)) {
        return *boolean ? "true" : "false";
    }
    return {};
}

bool truthy(const value& source) {
    if (const auto* boolean = std::get_if<bool>(&source)) {
        return *boolean;
    }
    if (const auto* number = std::get_if<double>(&source)) {
        return std::abs(*number) > 0.0000001;
    }
    if (const auto* text = std::get_if<std::string>(&source)) {
        return !text->empty();
    }
    return false;
}

const value& find_value(const runtime_state& runtime, const std::string& name) {
    static const value missing = std::monostate{};
    const auto iterator = runtime.variables.find(name);
    return iterator == runtime.variables.end() ? missing : iterator->second;
}

void execute_line(runtime_state& runtime, const std::string& line) {
    static const std::regex set_pattern(
        R"(^set\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.+)$)",
        std::regex::icase);
    static const std::regex add_pattern(
        R"(^add\s+([A-Za-z_][A-Za-z0-9_]*)\s+(.+)$)",
        std::regex::icase);
    static const std::regex unset_pattern(
        R"(^unset\s+([A-Za-z_][A-Za-z0-9_]*)$)",
        std::regex::icase);

    std::smatch match;
    if (std::regex_match(line, match, set_pattern)) {
        runtime.variables[match[1].str()] = parse_literal(match[2].str());
        return;
    }
    if (std::regex_match(line, match, add_pattern)) {
        const auto amount_value = parse_literal(match[2].str());
        double amount = 0;
        if (!as_number(amount_value, amount)) {
            throw std::runtime_error("add expects a number");
        }
        double current = 0;
        const auto iterator = runtime.variables.find(match[1].str());
        if (iterator != runtime.variables.end() && !as_number(iterator->second, current)) {
            throw std::runtime_error("add target is not a number");
        }
        runtime.variables[match[1].str()] = current + amount;
        return;
    }
    if (std::regex_match(line, match, unset_pattern)) {
        runtime.variables.erase(match[1].str());
        return;
    }

    throw std::runtime_error("unknown script command: " + line);
}

bool evaluate(runtime_state& runtime, std::string condition) {
    condition = trim(condition);
    if (condition.empty()) {
        return true;
    }

    static const std::regex identifier(R"(^[A-Za-z_][A-Za-z0-9_]*$)");
    static const std::regex comparison(
        R"(^([A-Za-z_][A-Za-z0-9_]*)\s*(==|!=|>=|<=|>|<)\s*(.+)$)");

    if (condition.front() == '!' && std::regex_match(condition.substr(1), identifier)) {
        return !truthy(find_value(runtime, condition.substr(1)));
    }
    if (std::regex_match(condition, identifier)) {
        return truthy(find_value(runtime, condition));
    }

    std::smatch match;
    if (!std::regex_match(condition, match, comparison)) {
        throw std::runtime_error("invalid condition: " + condition);
    }

    const auto& left = find_value(runtime, match[1].str());
    const auto right = parse_literal(match[3].str());
    const auto operation = match[2].str();
    double left_number = 0;
    double right_number = 0;
    if (as_number(left, left_number) && as_number(right, right_number)) {
        if (operation == "==") return left_number == right_number;
        if (operation == "!=") return left_number != right_number;
        if (operation == ">") return left_number > right_number;
        if (operation == ">=") return left_number >= right_number;
        if (operation == "<") return left_number < right_number;
        if (operation == "<=") return left_number <= right_number;
    }

    const auto comparison_result = as_string(left).compare(as_string(right));
    if (operation == "==") return comparison_result == 0;
    if (operation == "!=") return comparison_result != 0;
    if (operation == ">") return comparison_result > 0;
    if (operation == ">=") return comparison_result >= 0;
    if (operation == "<") return comparison_result < 0;
    if (operation == "<=") return comparison_result <= 0;
    return false;
}

template <typename callback>
int protect(runtime_state* runtime, callback action) {
    if (runtime == nullptr) {
        return 0;
    }
    try {
        runtime->last_error.clear();
        action();
        return 1;
    } catch (const std::exception& error) {
        runtime->last_error = error.what();
        return 0;
    }
}

}  // namespace

struct novel_runtime {
    runtime_state state;
};

novel_runtime* novel_runtime_create(void) {
    try {
        return new novel_runtime{};
    } catch (...) {
        return nullptr;
    }
}

void novel_runtime_destroy(novel_runtime* runtime) {
    delete runtime;
}

int novel_runtime_execute(novel_runtime* runtime, const char* script) {
    if (runtime == nullptr || script == nullptr) {
        return 0;
    }
    return protect(&runtime->state, [&] {
        std::istringstream lines(script);
        std::string source_line;
        while (std::getline(lines, source_line)) {
            auto line = trim(source_line);
            if (!line.empty() && line.front() != '#') {
                execute_line(runtime->state, line);
            }
        }
    });
}

int novel_runtime_evaluate(
    novel_runtime* runtime,
    const char* condition,
    int* result) {
    if (runtime == nullptr || condition == nullptr || result == nullptr) {
        return 0;
    }
    return protect(&runtime->state, [&] {
        *result = evaluate(runtime->state, condition) ? 1 : 0;
    });
}

const char* novel_runtime_last_error(const novel_runtime* runtime) {
    return runtime == nullptr ? "runtime is null" : runtime->state.last_error.c_str();
}

const char* novel_runtime_get_string(
    novel_runtime* runtime,
    const char* name) {
    if (runtime == nullptr || name == nullptr) {
        return "";
    }
    runtime->state.string_buffer = as_string(find_value(runtime->state, name));
    return runtime->state.string_buffer.c_str();
}

double novel_runtime_get_number(
    const novel_runtime* runtime,
    const char* name,
    double fallback) {
    if (runtime == nullptr || name == nullptr) {
        return fallback;
    }
    double result = 0;
    return as_number(find_value(runtime->state, name), result) ? result : fallback;
}

int novel_runtime_get_bool(
    const novel_runtime* runtime,
    const char* name,
    int fallback) {
    if (runtime == nullptr || name == nullptr) {
        return fallback;
    }
    const auto& source = find_value(runtime->state, name);
    return std::holds_alternative<std::monostate>(source)
        ? fallback
        : truthy(source) ? 1 : 0;
}
