"use strict";

const heroes = JSON.parse(data_heroes);
const prefixes = [
  "пикни героя",
  "выбери героя",
  "возьми героя",
  "запикай героя",
  "пик героя",
  "пикни",
  "выбери",
  "возьми",
  "запикай"
];

function normalize(value) {
  return value
    .toLowerCase()
    .replaceAll("ё", "е")
    .replace(/[^a-zа-я0-9' -]+/gi, " ")
    .replace(/\s+/g, " ")
    .trim();
}

function match(text) {
  const normalized = normalize(text);
  let spokenHero = null;
  for (const prefix of prefixes) {
    if (normalized.startsWith(prefix + " ")) {
      spokenHero = normalized.slice(prefix.length + 1).trim();
      break;
    }
  }

  if (!spokenHero) {
    return { matched: false };
  }

  const matches = heroes.filter(hero =>
    [hero.name, ...hero.aliases].some(alias => normalize(alias) === spokenHero));
  if (matches.length !== 1) {
    return { matched: false };
  }

  return {
    matched: true,
    confidence: 0.995,
    payload: { heroId: matches[0].id }
  };
}

function execute(payloadJson) {
  const payload = JSON.parse(payloadJson);
  const hero = heroes.find(candidate => candidate.id === payload.heroId);
  if (!hero) {
    throw new Error("Герой больше не существует в словаре плагина.");
  }

  return {
    capability: "dota2.hero_pick",
    entityId: hero.id,
    entityName: hero.name,
    value: hero.value
  };
}
