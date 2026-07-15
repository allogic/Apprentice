import fs from "node:fs";
import path from "node:path";

const project = path.resolve(import.meta.dirname, "..");
const nativeRoot = process.env.VS_PLAYER_ASSETS;
const native = nativeRoot
  ? path.join(nativeRoot, "game/shapes/entity/humanoid/seraphskinparts")
  : null;
const output = path.join(project, "assets/apprentice/shapes/entity/humanoid/races");
const requestedRaces = new Set(process.argv.slice(2));

function read(rel) {
  if (!native) {
    throw new Error(
      `Set VS_PLAYER_ASSETS to regenerate native-backed race parts (${rel})`
    );
  }
  return JSON.parse(fs.readFileSync(path.join(native, rel)));
}
function faces(texture = "#race") {
  const f = { texture, uv: [0, 0, 16, 16] };
  return { north: f, east: f, south: f, west: f, up: f, down: f };
}
function cube(name, from, to, rotation = {}, texture = "#race") {
  return {
    name, from, to, uv: [0, 0],
    rotationOrigin: rotation.origin ?? from,
    ...(rotation.x ? { rotationX: rotation.x } : {}),
    ...(rotation.y ? { rotationY: rotation.y } : {}),
    ...(rotation.z ? { rotationZ: rotation.z } : {}),
    faces: faces(texture)
  };
}
function faceRoot(race, children) {
  return {
    name: `${race}-face-root`, stepParentName: "Head",
    from: [0, 2.4, 2.5], to: [0, 2.4, 2.5],
    rotationOrigin: [0, 2.4, 2.5], faces: {}, children,
    attachmentpoints: [{
      code: `Apprentice-${race}-face`, posX: "0", posY: "0", posZ: "0",
      rotationX: "0", rotationY: "0", rotationZ: "0"
    }]
  };
}
function crownRoot(race, children) {
  return {
    name: `${race}-crown-root`, stepParentName: "Head",
    from: [3, 3.5, 2.5], to: [3, 3.5, 2.5],
    rotationOrigin: [3, 3.5, 2.5], faces: {}, children
  };
}
function compose(race, nativeParts, customChildren, crownChildren = [], textureOverrides = {}) {
  if (requestedRaces.size && !requestedRaces.has(race)) return;

  const shape = {
    textureWidth: 16, textureHeight: 16,
    textureSizes: { race: [16, 16] },
    textures: { race: `apprentice:entity/humanoid/races/${race}` },
    elements: []
  };
  nativeParts.forEach((rel, index) => {
    const part = read(rel);
    Object.assign(shape.textureSizes, part.textureSizes ?? {});
    Object.assign(shape.textures, part.textures ?? {});
    for (const element of part.elements ?? []) {
      element.name = `${race}-native-${index}-${element.name}`;
      shape.elements.push(element);
    }
  });
  Object.assign(shape.textures, textureOverrides);
  if (customChildren.length) shape.elements.push(faceRoot(race, customChildren));
  if (crownChildren.length) shape.elements.push(crownRoot(race, crownChildren));
  fs.writeFileSync(path.join(output, `${race}.json`), JSON.stringify(shape, null, 2) + "\n");
}

compose("human", [], []);
compose("elf", ["ears/pointy.json"], [
  cube("elf-cheek-left", [-0.05, -1.0, 1.25], [0.35, 0.25, 1.65], { z: -12 }),
  cube("elf-cheek-right", [-0.05, -1.0, -1.65], [0.35, 0.25, -1.25], { z: -12 })
]);
compose("dwarf", ["ears/round.json", "nose/segmented.json"], [
  cube("dwarf-brow", [-0.10, 0.45, -1.65], [0.45, 1.05, 1.65]),
  cube("dwarf-jaw", [-0.10, -1.65, -1.45], [0.45, -0.65, 1.45])
]);
compose("gnome", ["ears/pointy.json", "nose/pointy.json"], [
  cube("gnome-cheek-left", [-0.05, -1.15, 0.85], [0.35, -0.25, 1.45]),
  cube("gnome-cheek-right", [-0.05, -1.15, -1.45], [0.35, -0.25, -0.85])
]);
compose("halfling", ["ears/round.json", "nose/snub.json"], [
  cube("halfling-cheek-left", [-0.05, -1.15, 0.75], [0.40, -0.15, 1.55]),
  cube("halfling-cheek-right", [-0.05, -1.15, -1.55], [0.40, -0.15, -0.75])
]);
compose("goliath", [], [
  // A restrained stone-giant face: strong brow, broad nose, angular
  // cheekbones and jaw, plus small natural lithoderm ridges.
  cube("goliath-brow-left", [0.00, 0.35, 0.10], [0.55, 1.00, 2.25], {
    x: -8, origin: [0.25, 0.65, 0.10]
  }),
  cube("goliath-brow-right", [0.00, 0.35, -2.25], [0.55, 1.00, -0.10], {
    x: 8, origin: [0.25, 0.65, -0.10]
  }),
  cube("goliath-broad-nose", [0.02, -0.60, -0.68], [0.78, 0.48, 0.68]),
  cube("goliath-cheek-left", [0.00, -0.95, 1.25], [0.48, 0.18, 2.35], {
    x: -6, origin: [0.20, -0.35, 1.25]
  }),
  cube("goliath-cheek-right", [0.00, -0.95, -2.35], [0.48, 0.18, -1.25], {
    x: 6, origin: [0.20, -0.35, -1.25]
  }),
  cube("goliath-jaw-left", [0.00, -1.85, 0.62], [0.52, -0.72, 2.10]),
  cube("goliath-jaw-right", [0.00, -1.85, -2.10], [0.52, -0.72, -0.62]),
  cube("goliath-square-chin", [0.00, -2.08, -1.22], [0.55, -1.48, 1.22]),
  cube("goliath-lithoderm-center", [0.05, 1.22, -0.40], [0.52, 1.88, 0.40]),
  cube("goliath-lithoderm-left", [0.04, 1.12, 0.86], [0.44, 1.62, 1.36], {
    x: -8, origin: [0.20, 1.18, 0.90]
  }),
  cube("goliath-lithoderm-right", [0.04, 1.12, -1.36], [0.44, 1.62, -0.86], {
    x: 8, origin: [0.20, 1.18, -0.90]
  })
]);
compose("orc", [], [
  // Purpose-built ears avoid coupling the corrected tusks to a moustache
  // asset. The two-piece tusks grow vertically from the lower jaw.
  cube("orc-ear-left-base", [-0.35, -0.05, 1.95], [0.55, 0.95, 3.20], {
    x: -10, origin: [0.00, 0.35, 2.05]
  }),
  cube("orc-ear-left-tip", [-0.25, 0.25, 3.10], [0.45, 0.75, 4.35], {
    x: -12, origin: [0.00, 0.45, 3.10]
  }),
  cube("orc-ear-right-base", [-0.35, -0.05, -3.20], [0.55, 0.95, -1.95], {
    x: 10, origin: [0.00, 0.35, -2.05]
  }),
  cube("orc-ear-right-tip", [-0.25, 0.25, -4.35], [0.45, 0.75, -3.10], {
    x: 12, origin: [0.00, 0.45, -3.10]
  }),
  cube("orc-heavy-brow", [-0.02, 0.42, -2.10], [0.48, 0.92, 2.10]),
  cube("orc-tusk-left-base", [0.05, -1.90, 0.78], [0.66, -0.76, 1.28], {
    x: -5, origin: [0.34, -1.88, 1.03]
  }, "#bone"),
  cube("orc-tusk-left-tip", [0.12, -0.86, 0.88], [0.58, -0.15, 1.18], {
    x: -5, origin: [0.34, -0.86, 1.03]
  }, "#bone"),
  cube("orc-tusk-right-base", [0.05, -1.90, -1.28], [0.66, -0.76, -0.78], {
    x: 5, origin: [0.34, -1.88, -1.03]
  }, "#bone"),
  cube("orc-tusk-right-tip", [0.12, -0.86, -1.18], [0.58, -0.15, -0.88], {
    x: 5, origin: [0.34, -0.86, -1.03]
  }, "#bone")
], [], {
  bone: "apprentice:entity/humanoid/races/bone"
});
compose("dragonborn", [], [
  // The projected upper muzzle and separate lower jaw change both the side
  // profile and front silhouette. Cheek plates, brow plates, a forehead
  // ridge and backswept horns keep it recognisably draconic from the front.
  cube("dragonborn-brow-left", [0.00, 0.42, 0.08], [0.78, 1.12, 2.42], {
    x: -10, origin: [0.28, 0.70, 0.10]
  }),
  cube("dragonborn-brow-right", [0.00, 0.42, -2.42], [0.78, 1.12, -0.08], {
    x: 10, origin: [0.28, 0.70, -0.10]
  }),
  cube("dragonborn-forehead-plate", [0.02, 1.16, -0.72], [0.68, 2.30, 0.72]),
  cube("dragonborn-cheek-left", [0.00, -1.02, 1.30], [0.68, 0.36, 2.78], {
    x: -8, origin: [0.25, -0.25, 1.35]
  }),
  cube("dragonborn-cheek-right", [0.00, -1.02, -2.78], [0.68, 0.36, -1.30], {
    x: 8, origin: [0.25, -0.25, -1.35]
  }),
  cube("dragonborn-upper-muzzle", [0.10, -0.76, -1.48], [1.92, 0.22, 1.48]),
  cube("dragonborn-nose-cap", [1.78, -0.66, -1.22], [2.48, 0.10, 1.22]),
  cube("dragonborn-lower-jaw", [0.06, -1.68, -1.62], [1.78, -0.80, 1.62]),
  cube("dragonborn-jaw-tip", [1.58, -1.52, -1.36], [2.18, -0.86, 1.36]),
  cube("dragonborn-chin", [0.02, -2.02, -1.20], [0.92, -1.46, 1.20]),
  cube("dragonborn-horn-left-base", [0.02, 1.30, 1.52], [0.70, 2.86, 2.18], {
    x: -18, z: 18, origin: [0.34, 1.32, 1.84]
  }, "#bone"),
  cube("dragonborn-horn-left-tip", [0.08, 2.60, 1.68], [0.48, 3.90, 2.08], {
    x: -24, z: 24, origin: [0.28, 2.62, 1.88]
  }, "#bone"),
  cube("dragonborn-horn-right-base", [0.02, 1.30, -2.18], [0.70, 2.86, -1.52], {
    x: 18, z: 18, origin: [0.34, 1.32, -1.84]
  }, "#bone"),
  cube("dragonborn-horn-right-tip", [0.08, 2.60, -2.08], [0.48, 3.90, -1.68], {
    x: 24, z: 24, origin: [0.28, 2.62, -1.88]
  }, "#bone")
], [], {
  bone: "apprentice:entity/humanoid/races/bone"
});
compose("tiefling", ["ears/pointy.json", "ears/rust-horns.json"], [
  cube("tiefling-brow-left", [-0.10, 0.35, 0.05], [0.45, 1.00, 1.75], { z: -8 }),
  cube("tiefling-brow-right", [-0.10, 0.35, -1.75], [0.45, 1.00, -0.05], { z: 8 }),
  cube("tiefling-cheek-left", [-0.05, -1.20, 1.00], [0.35, 0.10, 1.55]),
  cube("tiefling-cheek-right", [-0.05, -1.20, -1.55], [0.35, 0.10, -1.00])
], [], {
  steel: "apprentice:entity/humanoid/races/bone"
});
