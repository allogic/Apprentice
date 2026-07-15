import fs from "node:fs";
import path from "node:path";

const root = path.resolve(import.meta.dirname, "..");
const shapeDir = path.join(root, "assets/apprentice/shapes/entity/humanoid/races");
const textureDir = path.join(root, "assets/apprentice/textures/entity/humanoid/races");
fs.mkdirSync(shapeDir, { recursive: true });
fs.mkdirSync(textureDir, { recursive: true });

const colors = {
  human: [172, 112, 76], elf: [92, 145, 91], dwarf: [126, 82, 50],
  gnome: [132, 96, 164], halfling: [176, 124, 70], orc: [72, 118, 55],
  goliath: [105, 112, 119], dragonborn: [112, 50, 42], tiefling: [112, 38, 80]
};

function cube(name, from, to, rotation = {}) {
  const face = { texture: "#race", uv: [0, 0, 16, 16] };
  return {
    name, from, to, uv: [0, 0],
    rotationOrigin: rotation.origin ?? from,
    ...(rotation.x ? { rotationX: rotation.x } : {}),
    ...(rotation.y ? { rotationY: rotation.y } : {}),
    ...(rotation.z ? { rotationZ: rotation.z } : {}),
    faces: { north: face, east: face, south: face, west: face, up: face, down: face }
  };
}

function anchor(name, parent, children, code) {
  return {
    name, stepParentName: parent, from: [0, 0, 0], to: [0, 0, 0], faces: {}, children,
    attachmentpoints: [{ code, posX: "0", posY: "0", posZ: "0", rotationX: "0", rotationY: "0", rotationZ: "0" }]
  };
}

const raceElements = {
  human: [
    anchor("humanHeadRoot", "Head", [cube("humanBrow", [-0.8, 2.5, -2.15], [0.0, 3.0, 2.15])], "ApprenticeHumanHead")
  ],
  elf: [
    anchor("elfHeadRoot", "Head", [
      cube("elfEarL", [-0.5, 0.2, 2.0], [1.2, 1.0, 4.8], { z: 18, origin: [-0.5, 0.6, 2.0] }),
      cube("elfEarR", [-0.5, 0.2, -4.8], [1.2, 1.0, -2.0], { z: 18, origin: [-0.5, 0.6, -2.0] })
    ], "ApprenticeElfHead")
  ],
  dwarf: [
    anchor("dwarfHeadRoot", "Head", [cube("dwarfBrow", [-1.0, 2.3, -2.3], [0.2, 3.2, 2.3])], "ApprenticeDwarfHead"),
    anchor("dwarfTorsoRoot", "UpperTorso", [
      cube("dwarfShoulderL", [-0.5, 1.5, 3.1], [2.0, 3.3, 5.0]),
      cube("dwarfShoulderR", [-0.5, 1.5, -5.0], [2.0, 3.3, -3.1])
    ], "ApprenticeDwarfBack")
  ],
  gnome: [
    anchor("gnomeHeadRoot", "Head", [
      cube("gnomeNose", [-2.3, 0.6, -0.6], [-0.4, 1.8, 0.6]),
      cube("gnomeEarL", [-0.3, 0.4, 2.0], [0.8, 1.2, 3.5]),
      cube("gnomeEarR", [-0.3, 0.4, -3.5], [0.8, 1.2, -2.0])
    ], "ApprenticeGnomeHead")
  ],
  halfling: [
    anchor("halflingHeadRoot", "Head", [
      cube("halflingEarL", [-0.5, 0.0, 2.0], [1.1, 1.5, 3.1]),
      cube("halflingEarR", [-0.5, 0.0, -3.1], [1.1, 1.5, -2.0])
    ], "ApprenticeHalflingHead")
  ],
  orc: [
    anchor("orcHeadRoot", "Head", [
      cube("orcBrow", [-1.0, 2.2, -2.4], [0.3, 3.2, 2.4]),
      cube("orcTuskL", [-2.0, -0.8, 0.8], [-0.5, 1.0, 1.35], { z: -12 }),
      cube("orcTuskR", [-2.0, -0.8, -1.35], [-0.5, 1.0, -0.8], { z: 12 })
    ], "ApprenticeOrcHead")
  ],
  goliath: [
    anchor("goliathHeadRoot", "Head", [cube("goliathCrown", [-0.5, 3.4, -2.0], [1.0, 4.2, 2.0])], "ApprenticeGoliathHead"),
    anchor("goliathTorsoRoot", "UpperTorso", [
      cube("goliathShoulderL", [-0.7, 1.0, 3.0], [2.3, 3.8, 5.4]),
      cube("goliathShoulderR", [-0.7, 1.0, -5.4], [2.3, 3.8, -3.0])
    ], "ApprenticeGoliathBack")
  ],
  dragonborn: [
    anchor("dragonbornHeadRoot", "Head", [
      cube("dragonbornMuzzle", [-2.8, 0.0, -1.4], [-0.3, 1.8, 1.4]),
      cube("dragonbornHornL", [0.2, 2.6, 1.0], [1.2, 5.5, 1.8], { z: -20 }),
      cube("dragonbornHornR", [0.2, 2.6, -1.8], [1.2, 5.5, -1.0], { z: 20 })
    ], "ApprenticeDragonbornHead"),
    anchor("dragonbornBackRoot", "LowerTorso", [cube("dragonbornTail", [1.0, -5.5, -0.7], [2.2, 0.0, 0.7], { z: 35, origin: [1.0, 0.0, 0.0] })], "ApprenticeDragonbornTail")
  ],
  tiefling: [
    anchor("tieflingHeadRoot", "Head", [
      cube("tieflingHornL", [0.0, 2.8, 0.8], [1.0, 6.2, 1.6], { z: -28 }),
      cube("tieflingHornR", [0.0, 2.8, -1.6], [1.0, 6.2, -0.8], { z: 28 })
    ], "ApprenticeTieflingHead"),
    anchor("tieflingBackRoot", "LowerTorso", [cube("tieflingTail", [0.8, -6.0, -0.45], [1.7, 0.0, 0.45], { z: 42, origin: [0.8, 0.0, 0.0] })], "ApprenticeTieflingTail")
  ]
};

for (const [race, elements] of Object.entries(raceElements)) {
  const shape = {
    textureWidth: 16, textureHeight: 16,
    textureSizes: { race: [16, 16] },
    textures: { race: `apprentice:entity/humanoid/races/${race}` },
    elements
  };
  fs.writeFileSync(path.join(shapeDir, `${race}.json`), JSON.stringify(shape, null, 2) + "\n");

  const [r, g, b] = colors[race];
  const ppm = [`P3`, `16 16`, `255`];
  for (let y = 0; y < 16; y++) {
    const row = [];
    for (let x = 0; x < 16; x++) {
      const shade = ((x + y) % 4 === 0) ? 18 : ((x * 3 + y) % 7 === 0 ? -14 : 0);
      row.push(`${Math.max(0, r + shade)} ${Math.max(0, g + shade)} ${Math.max(0, b + shade)}`);
    }
    ppm.push(row.join(" "));
  }
  fs.writeFileSync(path.join(textureDir, `${race}.ppm`), ppm.join("\n") + "\n");
}
