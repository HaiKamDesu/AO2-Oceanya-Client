using System.Text.Json.Nodes;

namespace AO2AIBot.Clients
{
    /// <summary>
    /// Provides the JSON schema used to grammar-constrain Ollama model output to valid AO2 client decisions.
    /// Passing this schema as the Ollama "format" value ensures the model can never produce malformed JSON
    /// or use invalid enum values for fields like textColor or shoutModifier.
    /// </summary>
    public static class AOClientResponseSchema
    {
        private const string SchemaJson = """
            {
              "type": "object",
              "properties": {
                "thinking": { "type": "string" },
                "shouldRespond": { "type": "boolean" },
                "channel": { "type": "string", "enum": ["IC", "OOC"] },
                "message": { "type": "string" },
                "state": {
                  "type": "object",
                  "properties": {
                    "textColor":      { "type": "string", "enum": ["white","green","red","orange","blue","yellow","magenta","cyan","gray"] },
                    "character":      { "type": "string" },
                    "emote":          { "type": "string" },
                    "position":       { "type": "string" },
                    "icShowname":     { "type": "string" },
                    "oocShowname":    { "type": "string" },
                    "area":           { "type": "string" },
                    "iniPuppetName":  { "type": "string" },
                    "sfx":            { "type": "string" },
                    "shoutModifier":  { "type": "string", "enum": ["nothing","holdIt","objection","takeThat","custom"] },
                    "effect":         { "type": "string", "enum": ["none","realization","hearts","reaction","impact"] },
                    "flip":                 { "type": "boolean" },
                    "additive":             { "type": "boolean" },
                    "immediate":            { "type": "boolean" },
                    "screenshake":          { "type": "boolean" },
                    "preanimEnabled":       { "type": "boolean" },
                    "selfOffsetHorizontal": { "type": "integer" },
                    "selfOffsetVertical":   { "type": "integer" }
                  }
                }
              },
              "required": ["thinking", "shouldRespond"]
            }
            """;

        /// <summary>
        /// Gets the parsed JSON schema node, safe to use as the Ollama "format" field value.
        /// </summary>
        public static JsonNode Schema { get; } = JsonNode.Parse(SchemaJson)!;
    }
}
