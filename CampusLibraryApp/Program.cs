using System.Security.Cryptography.X509Certificates;
using Raven.Client.Documents;
using Raven.Client.Documents.Linq;

var url = Environment.GetEnvironmentVariable("RAVENDB_URL")
    ?? throw new InvalidOperationException("Set RAVENDB_URL first.");
var database = Environment.GetEnvironmentVariable("RAVENDB_DATABASE")
    ?? throw new InvalidOperationException("Set RAVENDB_DATABASE first.");
var certPath = Environment.GetEnvironmentVariable("RAVENDB_CERT_PATH")
    ?? throw new InvalidOperationException("Set RAVENDB_CERT_PATH first.");
var certPassword = Environment.GetEnvironmentVariable("RAVENDB_CERT_PASSWORD") ?? "";

using var certificate = new X509Certificate2(certPath, certPassword);
using var store = new DocumentStore { Urls = new[] { url }, Database = database, Certificate = certificate }.Initialize();

Console.WriteLine("\n=== CAMPUS LIBRARY MANAGEMENT SYSTEM ===");

while (true)
{
    Console.WriteLine("""

    1. Add book
    2. View books
    3. Search books by title
    4. Register student
    5. View students
    6. Issue book
    7. Return book
    8. View issued loans
    9. Insert demo data
    0. Exit
    """);
    Console.Write("Choose: ");
    var choice = Console.ReadLine()?.Trim();
    try
    {
        switch (choice)
        {
            case "1": await AddBook(store); break;
            case "2": await ViewBooks(store); break;
            case "3": await SearchBooks(store); break;
            case "4": await RegisterStudent(store); break;
            case "5": await ViewStudents(store); break;
            case "6": await IssueBook(store); break;
            case "7": await ReturnBook(store); break;
            case "8": await ViewIssuedLoans(store); break;
            case "9": await InsertDemoData(store); break;
            case "0": return;
            default: Console.WriteLine("Choose a valid option."); break;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"ERROR: {ex.Message}");
    }
}

static async Task AddBook(IDocumentStore store)
{
    Console.Write("Title: "); var title = Required();
    Console.Write("Author: "); var author = Required();
    Console.Write("Category: "); var category = Required();
    Console.Write("Total copies: ");
    if (!int.TryParse(Console.ReadLine(), out var copies) || copies < 1) { Console.WriteLine("Enter a positive number."); return; }
    var book = new Book { Title = title, Author = author, Category = category, TotalCopies = copies, AvailableCopies = copies };
    using var s = store.OpenAsyncSession();
    await s.StoreAsync(book); await s.SaveChangesAsync();
    Console.WriteLine($"Book saved. ID: {book.Id}");
}

static async Task ViewBooks(IDocumentStore store)
{
    using var s = store.OpenAsyncSession();
    var books = await s.Query<Book>().OrderBy(b => b.Title).ToListAsync();
    Console.WriteLine($"Books found: {books.Count}");
    foreach (var b in books) Console.WriteLine($"{b.Id} | {b.Title} | {b.Author} | {b.Category} | Available {b.AvailableCopies}/{b.TotalCopies}");
}

static async Task SearchBooks(IDocumentStore store)
{
    Console.Write("Title contains: "); var term = Required();
    using var s = store.OpenAsyncSession();
    var books = await s.Query<Book>().Where(b => b.Title.Contains(term)).ToListAsync();
    foreach (var b in books) Console.WriteLine($"{b.Id} | {b.Title} | {b.Author} | Available {b.AvailableCopies}/{b.TotalCopies}");
    if (books.Count == 0) Console.WriteLine("No matching books.");
}

static async Task RegisterStudent(IDocumentStore store)
{
    Console.Write("Full name: "); var name = Required();
    Console.Write("Student ID: "); var studentId = Required();
    Console.Write("Department: "); var department = Required();
    Console.Write("Email: "); var email = Required();
    using var s = store.OpenAsyncSession();
    if (await s.Query<Student>().FirstOrDefaultAsync(x => x.StudentId == studentId) is not null) { Console.WriteLine("Student ID already exists."); return; }
    var student = new Student { Name = name, StudentId = studentId, Department = department, Email = email };
    await s.StoreAsync(student); await s.SaveChangesAsync();
    Console.WriteLine($"Student registered. ID: {student.Id}");
}

static async Task ViewStudents(IDocumentStore store)
{
    using var s = store.OpenAsyncSession();
    var students = await s.Query<Student>().OrderBy(x => x.Name).ToListAsync();
    Console.WriteLine($"Students found: {students.Count}");
    foreach (var x in students) Console.WriteLine($"{x.Id} | {x.StudentId} | {x.Name} | {x.Department} | {x.Email}");
}

static async Task IssueBook(IDocumentStore store)
{
    Console.Write("Student ID: "); var studentId = Required();
    Console.Write("Book document ID (e.g. books/1-A): "); var bookId = Required();
    using var s = store.OpenAsyncSession();
    var student = await s.Query<Student>().FirstOrDefaultAsync(x => x.StudentId == studentId);
    var book = await s.LoadAsync<Book>(bookId);
    if (student is null) { Console.WriteLine("Student not found. Register first."); return; }
    if (book is null) { Console.WriteLine("Book not found."); return; }
    if (book.AvailableCopies < 1) { Console.WriteLine("No copies available."); return; }
    book.AvailableCopies--;
    var loan = new Loan { StudentId = student.StudentId, StudentName = student.Name, StudentDocumentId = student.Id!, BookId = book.Id!, BookTitle = book.Title, IssueDate = DateTime.UtcNow.Date, DueDate = DateTime.UtcNow.Date.AddDays(7), Status = "Issued" };
    await s.StoreAsync(loan); await s.SaveChangesAsync();
    Console.WriteLine($"Issued '{book.Title}' to {student.Name}. Loan ID: {loan.Id}");
}

static async Task ReturnBook(IDocumentStore store)
{
    Console.Write("Loan document ID: "); var id = Required();
    using var s = store.OpenAsyncSession();
    var loan = await s.LoadAsync<Loan>(id);
    if (loan is null) { Console.WriteLine("Loan not found."); return; }
    if (loan.Status == "Returned") { Console.WriteLine("Already returned."); return; }
    var book = await s.LoadAsync<Book>(loan.BookId);
    loan.Status = "Returned"; loan.ReturnDate = DateTime.UtcNow.Date;
    if (book is not null) book.AvailableCopies = Math.Min(book.TotalCopies, book.AvailableCopies + 1);
    await s.SaveChangesAsync();
    Console.WriteLine($"Returned '{loan.BookTitle}'.");
}

static async Task ViewIssuedLoans(IDocumentStore store)
{
    using var s = store.OpenAsyncSession();
    var loans = await s.Query<Loan>().Where(x => x.Status == "Issued").OrderBy(x => x.DueDate).ToListAsync();
    Console.WriteLine($"Issued loans: {loans.Count}");
    foreach (var x in loans) Console.WriteLine($"{x.Id} | {x.StudentName} ({x.StudentId}) | {x.BookTitle} | Due {x.DueDate:yyyy-MM-dd}");
}

static async Task InsertDemoData(IDocumentStore store)
{
    using var s = store.OpenAsyncSession();
    if (await s.LoadAsync<Book>("books/1-A") is null)
        await s.StoreAsync(new Book { Id = "books/1-A", Title = "Clean Code", Author = "Robert C. Martin", Category = "Programming", TotalCopies = 2, AvailableCopies = 2 });
    if (await s.LoadAsync<Book>("books/2-A") is null)
        await s.StoreAsync(new Book { Id = "books/2-A", Title = "Introduction to Algorithms", Author = "Thomas H. Cormen", Category = "Algorithms", TotalCopies = 3, AvailableCopies = 3 });
    if (await s.LoadAsync<Student>("students/1-A") is null)
        await s.StoreAsync(new Student { Id = "students/1-A", Name = "Aarav Sharma", StudentId = "CSE2026001", Department = "CSE", Email = "aarav@example.com" });
    if (await s.LoadAsync<Student>("students/2-A") is null)
        await s.StoreAsync(new Student { Id = "students/2-A", Name = "Priya Nair", StudentId = "CSE2026002", Department = "CSE", Email = "priya@example.com" });
    await s.SaveChangesAsync();
    Console.WriteLine("Demo data inserted; existing documents were not overwritten.");
}

static string Required()
{
    while (true) { var value = Console.ReadLine()?.Trim(); if (!string.IsNullOrWhiteSpace(value)) return value; Console.Write("Required, try again: "); }
}

public sealed class Book { public string? Id { get; set; } public string Title { get; set; } = ""; public string Author { get; set; } = ""; public string Category { get; set; } = ""; public int TotalCopies { get; set; } public int AvailableCopies { get; set; } }
public sealed class Student { public string? Id { get; set; } public string Name { get; set; } = ""; public string StudentId { get; set; } = ""; public string Department { get; set; } = ""; public string Email { get; set; } = ""; }
public sealed class Loan { public string? Id { get; set; } public string StudentId { get; set; } = ""; public string StudentName { get; set; } = ""; public string StudentDocumentId { get; set; } = ""; public string BookId { get; set; } = ""; public string BookTitle { get; set; } = ""; public DateTime IssueDate { get; set; } public DateTime DueDate { get; set; } public DateTime? ReturnDate { get; set; } public string Status { get; set; } = "Issued"; }
