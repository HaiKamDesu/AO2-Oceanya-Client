using System.Text.Json.Nodes;

namespace AO2AIBot.Clients
{
    /// <summary>
    /// Provides the JSON schema for grammar-constraining Ollama model output
    /// to the strict action-array response contract.
    /// </summary>
    public static class AOClientResponseSchema
    {
        private const string SchemaJson = """
            {
              "type": "object",
              "properties": {
                "shouldRespond": { "type": "boolean" },
                "actions": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "properties": {
                      "type": {
                        "type": "string",
                        "enum": [
                          "speak", "set_ic_showname", "set_ooc_showname", "set_character",
                          "set_position", "set_text_color", "set_flip", "set_additive",
                          "set_immediate", "set_preanim_enabled", "set_area", "set_ini_puppet",
                          "set_offset", "set_emote", "set_sfx", "set_desk_mod", "set_effect",
                          "set_screenshake", "set_shout_modifier", "set_emote_modifier"
                        ]
                      },
                      "value": {},
                      "channel": { "type": "string", "enum": ["IC", "OOC"] },
                      "message": { "type": "string" },
                      "emote": { "type": "string" },
                      "textColor": { "type": "string", "enum": ["white","green","red","orange","blue","yellow","magenta","cyan","gray"] },
                      "shoutModifier": { "type": "string", "enum": ["nothing","holdIt","objection","takeThat","custom"] },
                      "effect": { "type": "string", "enum": ["none","realization","hearts","reaction","impact"] },
                      "screenshake": { "type": "boolean" },
                      "sfx": { "type": "string" },
                      "horizontal": { "type": "integer" },
                      "vertical": { "type": "integer" }
                    },
                    "required": ["type"]
                  }
                }
              },
              "required": ["shouldRespond", "actions"]
            }
            """;

        /// <summary>
        /// Gets the parsed JSON schema node, safe to use as the Ollama "format" field value.
        /// </summary>
        public static JsonNode Schema { get; } = JsonNode.Parse(SchemaJson)!;
    }
}
