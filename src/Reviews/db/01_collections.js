// =====================================================================
// Проєкт №3. Мікросервіс «Відгуки та обговорення фільмів» кінотеатру «Кадр»
// Скрипт 01: колекції з JSON Schema Validation (ідемпотентний: створює або оновлює валідатор)
// Запуск: mongosh "mongodb://localhost:27017" 01_collections.js
// =====================================================================
db = db.getSiblingDB("cinema_reviews");

// mongosh записує числа як double, тому для цілих — "number" + multipleOf: 1
const rating = { bsonType: "number", minimum: 1, maximum: 5, multipleOf: 1 };
const author = {
  bsonType: "object",
  required: ["customerId", "displayName"],
  properties: {
    customerId: { bsonType: "number", description: "Customers.Id у CinemaBooking (без FK)" },
    displayName: { bsonType: "string", maxLength: 60 }
  }
};

const schemas = {
  // reviews: різні види відгуків в одній колекції (kind = quick | full | critic)
  reviews: {
    bsonType: "object",
    required: ["movieId", "movieTitle", "author", "kind", "rating", "createdAt"],
    properties: {
      movieId: { bsonType: "number", description: "Movies.Id у CinemaCatalog (без FK)" },
      movieTitle: { bsonType: "string", description: "денормалізовано з Каталогу для читання" },
      author: author,
      kind: { enum: ["quick", "full", "critic"] },
      rating: rating,
      title: { bsonType: "string", maxLength: 120 },
      text: { bsonType: "string", maxLength: 3000 },
      hasSpoilers: { bsonType: "bool" },
      photos: { bsonType: "array", maxItems: 5, items: { bsonType: "string" } },
      publication: { bsonType: "string" },
      comments: {                         // вбудований масив — кілька коментарів, завжди потрібні разом з відгуком
        bsonType: "array",
        maxItems: 20,
        items: {
          bsonType: "object",
          required: ["author", "text", "createdAt"],
          properties: { author: { bsonType: "string" }, text: { bsonType: "string" }, createdAt: { bsonType: "date" } }
        }
      },
      discussionId: { bsonType: "objectId", description: "reference на discussions" },
      likes: { bsonType: "number", minimum: 0 },
      createdAt: { bsonType: "date" }
    }
  },

  // discussions: тема обговорення фільму; останні повідомлення вбудовано (гібрид), усі — у messages
  discussions: {
    bsonType: "object",
    required: ["movieId", "movieTitle", "title", "author", "stats", "createdAt"],
    properties: {
      movieId: { bsonType: "number" },
      movieTitle: { bsonType: "string" },
      title: { bsonType: "string", minLength: 3, maxLength: 150 },
      author: author,
      tags: { bsonType: "array", items: { bsonType: "string" } },
      isPinned: { bsonType: "bool" },
      stats: {
        bsonType: "object",
        required: ["messagesCount"],
        properties: { messagesCount: { bsonType: "number", minimum: 0 }, lastActivityAt: { bsonType: "date" } }
      },
      lastMessages: { bsonType: "array", maxItems: 3 },
      createdAt: { bsonType: "date" }
    }
  },

  // messages: необмежено багато повідомлень обговорення — окрема колекція (reference)
  messages: {
    bsonType: "object",
    required: ["discussionId", "author", "text", "createdAt"],
    properties: {
      discussionId: { bsonType: "objectId" },
      author: author,
      text: { bsonType: "string", minLength: 1, maxLength: 2000 },
      replyTo: { bsonType: ["objectId", "null"] },
      createdAt: { bsonType: "date" }
    }
  },

  // movieRatings: read model — готовий рейтинг фільму (денормалізація для швидкого читання)
  movieRatings: {
    bsonType: "object",
    required: ["_id", "movieTitle", "average", "count"],
    properties: {
      _id: { bsonType: "number", description: "= Movies.Id" },
      movieTitle: { bsonType: "string" },
      average: { bsonType: "number", minimum: 0, maximum: 5 },
      count: { bsonType: "number", minimum: 0 },
      distribution: { bsonType: "object" },
      updatedAt: { bsonType: "date" }
    }
  }
};

const existing = db.getCollectionNames();
for (const [name, schema] of Object.entries(schemas)) {
  if (existing.includes(name)) {
    db.runCommand({ collMod: name, validator: { $jsonSchema: schema }, validationLevel: "strict" });
    print(`${name}: валідатор оновлено`);
  } else {
    db.createCollection(name, { validator: { $jsonSchema: schema }, validationLevel: "strict" });
    print(`${name}: створено`);
  }
}
