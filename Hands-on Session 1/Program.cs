using Hands_on_Session_1.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml.Serialization;
using static System.Net.WebRequestMethods;
using File = System.IO.File;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Configs
string inputCsv = "BangDiem.csv";
int scoreColumnCapacity = 10;
string mainDomain = "http://localhost:5000";
string getStudentListApi = $"{mainDomain}/api/students/stream";
string getGradeSchemasList = $"{mainDomain}/api/grade-schema";
string summaryReportName = "ReportAll.csv";
string gradeSchemasName = "GradeSchemas.csv";
string reportDirectory = "BaoCao";

HttpClient client = new();


app.MapGet("/api/grade-schema/{classNumber}", async (string classNumber) =>
{
    string fileName = $"{classNumber}.csv";

    //check legit file
    if (!File.Exists(fileName))
    {
        return Results.NotFound(new { message = $"Không tìm thấy schema cho khối {classNumber} (Thiếu file {fileName})" });
    }

    try
    {
        var lines = File.ReadAllLines(fileName).Skip(1);

        var schemas = lines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line =>
            {
                var parts = line.Split(',');
                return new GradeSchema
                {
                    ClassName = parts[0].Trim(),
                    MinScore = double.Parse(parts[1], CultureInfo.InvariantCulture),
                    MaxScore = double.Parse(parts[2], CultureInfo.InvariantCulture),
                    Grade = parts[3].Trim()
                };
            })
            .ToList();


        return Results.Ok(schemas);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { message = "Lỗi định dạng file CSV", error = ex.Message });
    }
});

app.MapGet("/api/grade-schema", async () =>
{
    string fileName = gradeSchemasName;

    //check legit file
    if (!File.Exists(fileName))
    {
        return Results.NotFound(new { message = $"Có lỗi xảy ra! Không tìm thấy file!" });
    }

    try
    {
        var lines = File.ReadAllLines(fileName).Skip(1);

        var schemas = lines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line =>
            {
                var parts = line.Split(',');
                return new GradeSchema
                {
                    ClassName = parts[0].Trim(),
                    MinScore = double.Parse(parts[1], CultureInfo.InvariantCulture),
                    MaxScore = double.Parse(parts[2], CultureInfo.InvariantCulture),
                    Grade = parts[3].Trim()
                };
            })
            .ToList();


        return Results.Ok(schemas);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { message = "Lỗi định dạng file CSV", error = ex.Message });
    }
});

app.MapGet("/api/students", async () =>
{
    var stopwatch = Stopwatch.StartNew();

    string fileName = inputCsv;

    if (!File.Exists(fileName))
    {
        return Results.NotFound(new { message = "File not found" });
    }

    try
    {
        var lines = File.ReadAllLines(fileName).Skip(1);

        var students = lines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line =>
            {
                var parts = line.Split(',');

                var scores = parts.Skip(3)
                                  .Select(scoreStr => double.Parse(scoreStr.Trim(), CultureInfo.InvariantCulture))
                                  .ToList();

                return new Student
                {
                    Name = parts[0].Trim(),
                    Class = parts[1].Trim(),
                    Gender = parts[2].Trim(),
                    Scores = scores,
                    AverageScore = scores.Count != 0 ? Math.Round(scores.Average(), 2) : 0
                };
            })
            .ToList();


        stopwatch.Stop();

        Console.WriteLine($"[PERFORMANCE] Reading time: {stopwatch.ElapsedMilliseconds} ms");

        return Results.Ok(students);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { message = "Lỗi xảy ra khi đọc file: ", error = ex.Message });
    }
});

app.MapGet("/api/students/stream", async () =>
{
    var stopwatch = Stopwatch.StartNew();

    //using stream pipeline for this
    //stream traverse through each element, doing the jobs, then free it from RAM

    string fileName = inputCsv;

    if (!File.Exists(fileName))
    {
        return Results.NotFound(new { message = "File not found" });
    }

    try
    {
        //can estimate the expected capacity of the list, helping c# to not resize the list continously
        var studentList = new List<Student>();

        using var reader = new StreamReader(fileName);

        //skip the 1st line
        await reader.ReadLineAsync();

        string currentLine;
        while ((currentLine = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrEmpty(currentLine)) continue;

            var parts = currentLine.Split(',');

            var scores = new List<double>(scoreColumnCapacity);
            double sumScore = 0;

            for (int i = 3; i < parts.Length; i++)
            {
                double score = double.Parse(parts[i].Trim(), CultureInfo.InvariantCulture);
                scores.Add(score);
                sumScore += score;
            }

            studentList.Add(new Student
            {
                Name = parts[0].Trim(),
                Class = parts[1].Trim(),
                Gender = parts[2].Trim(),
                Scores = scores,
                AverageScore = scores.Count > 0 ? Math.Round(sumScore / scores.Count, 2) : 0
            });

        }

        stopwatch.Stop();

        // 3. In kết quả ra màn hình Console (Đơn vị: mili-giây)
        Console.WriteLine($"[PERFORMANCE] Thời gian đọc và xử lý 3000 dòng: {stopwatch.ElapsedMilliseconds} ms");


        return Results.Ok(studentList);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { message = "Lỗi xảy ra khi đọc file: ", error = ex.Message });
    }
});

app.MapPost("/api/report", async () =>
{
    if (!Directory.Exists(reportDirectory))
    {
        Directory.CreateDirectory(reportDirectory);
    }


    //all the reports file to be created
    var generatedReports = new List<string>();

    try
    {
        List<Student> studentList;
        var gradeList = new List<GradeSchema>();

        studentList = await client.GetFromJsonAsync<List<Student>>(getStudentListApi);

        if (studentList == null || studentList.Count == 0)
            return Results.BadRequest(new { message = "Không lấy được dữ liệu danh sách học sinh!" });


        gradeList = await GetGradeListFromStudentClass(studentList);

        //calculate student grade
        calculateStudentGrade(studentList, gradeList);

        // group the classes
        var groupedByClass = studentList.GroupBy(s => s.Class);

        foreach (var classGroup in groupedByClass)
        {
            //create new report for each class
            var classReport = new StringBuilder();
            classReport.AppendLine("Lớp,Tên,Giới Tính,Điểm TB,Xếp Loại");

            foreach (var student in classGroup)
            {
                //add report inf
                classReport.AppendLine($"{student.Class},{student.Name},{student.Gender},{student.AverageScore},{student.FinalGrade}");
            }

            //final sum for each class
            var summaries = classGroup.GroupBy(s => new { s.Gender, s.FinalGrade })
                                      .OrderBy(g => g.Key.FinalGrade);

            //add each sum to the end of the report
            foreach (var sum in summaries)
            {
                classReport.AppendLine($"Lớp {classGroup.Key} có {sum.Count()} học sinh {sum.Key.Gender} có điểm {sum.Key.FinalGrade}");
            }

            classReport.AppendLine();

            //export to csv
            string classReportName = $"{classGroup.Key}_Report.csv";

            //put the file to the folder
            string fullFilePath = Path.Combine(reportDirectory, classReportName);

            await File.WriteAllTextAsync(fullFilePath, classReport.ToString(), Encoding.UTF8);


            generatedReports.Add(fullFilePath);
        }

    }
    catch (HttpRequestException httpReqEx)
    {
        return Results.Problem(title: "Lỗi kết nối đến API bên thứ 3.",
            detail: httpReqEx.Message,
            statusCode: 503
            );
    }
    catch (IOException ioEx)
    {
        return Results.Problem(title: "Lỗi đọc file!",
            detail: ioEx.Message,
            statusCode: 500);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { message = "Có lỗi xảy ra trong quá trình xử lý!", detail = ex.Message });
    }

    return Results.Ok(new { message = "Báo cáo đã được tạo!", files = generatedReports });

});

app.MapPost("/api/report/all", async () =>
{
    List<Student> studentList = [];
    List<GradeSchema> gradeList = [];
    try
    {
        studentList = await client.GetFromJsonAsync<List<Student>>(getStudentListApi);

        if (studentList == null || studentList.Count == 0)
        {
            return Results.Problem(
                title: "Dữ liệu trống",
                detail: "Không lấy được danh sách học sinh.",
                statusCode: 400);
        }

        gradeList = await GetGradeListFromStudentClass(studentList);
        if (gradeList == null || gradeList.Count == 0)
            return Results.Problem(title: "Có lỗi xãy ra trong quá trình lấy danh sách, hoặc danh sách hiện đang rỗng!",
                detail: "",
                statusCode: 204);

        //calculate student grade
        calculateStudentGrade(studentList, gradeList);

        //report
        var classReport = new StringBuilder();
        classReport.AppendLine("Lớp,Số lượng,Giới tính,Xếp loại");

        var groupedByClass = studentList.GroupBy(s => s.Class);

        foreach (var studentClass in groupedByClass)
        {
            int studentCount = studentClass.Count();

            var summaries = studentClass.GroupBy(s => new { s.Gender, s.FinalGrade })
                                      .OrderBy(g => g.Key.FinalGrade);

            bool isFirstRowOfClass = true;

            foreach (var sum in summaries)
            {
                string className = isFirstRowOfClass ? studentClass.Key : "";

                classReport.AppendLine($"{className},{sum.Count()},{sum.Key.Gender},{sum.Key.FinalGrade}");

                isFirstRowOfClass = false;
            }

            classReport.AppendLine();
        }

        await File.WriteAllTextAsync(summaryReportName, classReport.ToString(), Encoding.UTF8);

        return Results.Ok(new { message = "Đã thành công tạo file báo cáo", file = classReport });
    }
    catch (OperationCanceledException)
    {
        throw;
    }
    catch (Exception)
    {
        throw;
    }
});



app.MapGet("/test", async () =>
{
    List<Student> studentList = await client.GetFromJsonAsync<List<Student>>("http://localhost:5000/api/students");

    // with each class number, can be 12, 11 or 10, there will be 3 apis to get the grade
    //var classNumbers = studentList.Select(s => s.Class.Substring(0, 2)).Distinct();
    //var allSchemas = new List<GradeSchema>();

    //foreach (var num in classNumbers)
    //{
    //    //get all the grade
    //    var schemas = await client.GetFromJsonAsync<List<GradeSchema>>($"http://localhost:5000/api/grade-schema/{num}");
    //    if (schemas != null) allSchemas.AddRange(schemas);
    //}



    //return Results.Ok($"{allSchemas[0].MinScore.GetType()} - {studentList[0].AverageScore.GetType()}");


    List<GradeSchema> gradeList = await GetGradeListFromStudentClass(studentList);

    //calculateStudentGrade(studentList, gradeList);

    //if (studentList[0].FinalGrade == null)
    //{
    //    Console.WriteLine("Errorrrrrrrrrrrrrrrrrrrrrrrrrrr");
    //}

    //Dictionary<string, List<GradeSchema>> gradeDict = gradeList
    //    .GroupBy(g => g.ClassName)
    //    .ToDictionary(g => g.Key, g => g.ToList());

    //if (gradeDict.TryGetValue("12", out var classSchemas))
    //{
    //    var check = classSchemas.FirstOrDefault(sc =>
    //    8.6 >= sc.MinScore &&
    //    8.6 <= sc.MaxScore)?.Grade ?? "F";

    //    return Results.Ok(check);
    //}

    //var task1 = DoStuff1();
    //var task2 = DoStuff2();

    //var doTask1 = await task1;
    //var doTask2 = await task2;


    return Results.Ok(gradeList);
});

app.MapGet("/api/data-generator", () =>
{
    Console.WriteLine("Bắt đầu tạo mock data...");

    var rnd = new Random();
    string studentFile = "BangDiem.csv";
    string schemaFile = "GradeSchemas.csv";

    // ==========================================
    // 1. TẠO DỮ LIỆU SCHEMA (KHỐI 1 ĐẾN 12)
    // ==========================================
    using (var schemaWriter = new StreamWriter(schemaFile, append: false, Encoding.UTF8))
    {
        schemaWriter.WriteLine("ClassName,MinScore,MaxScore,Grade");

        for (int i = 1; i <= 12; i++)
        {
            string block = i.ToString();
            // Mỗi khối có 5 mốc điểm
            schemaWriter.WriteLine($"{block},8.5,10.0,A");
            schemaWriter.WriteLine($"{block},7.0,8.49,B");
            schemaWriter.WriteLine($"{block},5.5,6.99,C");
            schemaWriter.WriteLine($"{block},4.0,5.49,D");
            schemaWriter.WriteLine($"{block},0.0,3.99,F");
        }
    }
    Console.WriteLine($"Đã tạo xong file Schema: {schemaFile}");

    // ==========================================
    // 2. TẠO DỮ LIỆU HỌC SINH (3000 RECORDS)
    // ==========================================
    string[] ho = { "Nguyễn", "Trần", "Lê", "Phạm", "Hoàng", "Huỳnh", "Phan", "Vũ", "Võ", "Đặng", "Bùi", "Đỗ" };
    string[] demNam = { "Văn", "Hữu", "Đình", "Minh", "Gia", "Thái", "Tuấn", "Hoàng", "Đức" };
    string[] tenNam = { "Phong", "Minh", "Đạt", "Bảo", "Hải", "Sơn", "Long", "Dũng", "Thắng", "Huy", "Khoa" };
    string[] demNu = { "Thị", "Ngọc", "Thu", "Mai", "Diễm", "Phương", "Bích", "Thanh", "Như" };
    string[] tenNu = { "Lan", "Hương", "Trang", "Linh", "Thảo", "Vy", "Nhung", "Trâm", "Nhi", "Hà", "Yến" };
    string[] blockNames = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12" };
    string[] classSuffixes = { "A1", "A2", "A3", "B1", "B2", "C1" };

    using (var studentWriter = new StreamWriter(studentFile, append: false, Encoding.UTF8))
    {
        // Tạo Header
        studentWriter.WriteLine("Tên,Lớp,Giới tính,Diem1,Diem2,Diem3,Diem4,Diem5,Diem6,Diem7,Diem8,Diem9,Diem10");

        for (int i = 0; i < 3000; i++)
        {
            // Random Giới tính
            bool isMale = rnd.Next(2) == 0;
            string gender = isMale ? "Nam" : "Nữ";

            // Random Tên
            string fullName = isMale
                ? $"{ho[rnd.Next(ho.Length)]} {demNam[rnd.Next(demNam.Length)]} {tenNam[rnd.Next(tenNam.Length)]}"
                : $"{ho[rnd.Next(ho.Length)]} {demNu[rnd.Next(demNu.Length)]} {tenNu[rnd.Next(tenNu.Length)]}";

            // Random Lớp (VD: 12A1, 5B2)
            string className = blockNames[rnd.Next(blockNames.Length)] + classSuffixes[rnd.Next(classSuffixes.Length)];

            // Random 10 cột điểm (Bước nhảy 0.25)
            // rnd.Next(0, 41) cho ra số từ 0 -> 40. Nhân với 0.25 sẽ ra 0.0, 0.25, 0.5... đến 10.0
            var scores = new List<double>();
            for (int j = 0; j < 10; j++)
            {
                double score = rnd.Next(0, 41) * 0.25;
                scores.Add(score);
            }

            // Ghi xuống file
            studentWriter.WriteLine($"{fullName},{className},{gender},{string.Join(",", scores)}");
        }
    }
    Console.WriteLine($"Đã tạo xong file Học sinh: {studentFile} với 3000 records!");
});

async Task<List<GradeSchema>> GetGradeListFromStudentClass(List<Student> studentList)
{
    // with each class number, can be 12, 11 or 10, there will be 3 apis to get the grade
    // also add where condition to make sure there is no exception for SubString below
    // may skip the fraud data since it does not match the condition, if can, throw OutOfRangeArgument for SubString

    try
    {
        List<GradeSchema> gradeList = [];

        var classNumbers = studentList
            .Where(s => !string.IsNullOrEmpty(s.Class))
            .Select(s => new string([.. s.Class.TakeWhile(char.IsDigit)]))
            .Where(num => !string.IsNullOrEmpty(num))
            .Distinct()
            .ToList();


        //this is linear task
        //it traverse through every elements in classNumbers class
        //second iteration will run only when the 1st is done, even with the await api calling inside
        //foreach (var num in classNumbers)
        //{
        //    //get all the grade
        //    var grades = await client.GetFromJsonAsync<List<GradeSchema>>($"{getGradeSchemasList}/{num}");
        //    if (grades != null) gradeList.AddRange(grades);
        //}

        //using concurrent requests
        //var apiTasks = classNumbers.Select(num => client.GetFromJsonAsync<List<GradeSchema>>($"{getGradeSchemasList}/{num}));

        //var results = await Task.WhenAll(apiTasks);

        //gradeList = results.Where(r => r != null).SelectMany(r => r).ToList();

        var allGrades = await client.GetFromJsonAsync<List<GradeSchema>>(getGradeSchemasList);

        if (allGrades == null || allGrades.Count == 0)
        {
            return [];
        }

        var filteredGrades = allGrades
            .Where(schema => classNumbers.Contains(schema.ClassName))
            .ToList();

        return filteredGrades;
    }
    catch (Exception)
    {
        throw;
    }

}

void calculateStudentGrade(List<Student> studentList, List<GradeSchema> gradeList)
{
    //using FirstOrDefault linq each iteration in a loop
    //may cause CPU wasting
    //since in each iteration, it will need to traverse back from the BEGINING to find exact values (<grade> in this case)
    //make <gradeList> from List to Dictionary
    Dictionary<string, List<GradeSchema>> gradeDict = gradeList
        .GroupBy(g => g.ClassName)
        .ToDictionary(g => g.Key, g => g.ToList());

    //assign grade to student
    //may make a function fot this
    foreach (var student in studentList)
    {
        //var grade = gradeList.FirstOrDefault(sc =>
        //    sc.ClassName == student.Class &&
        //    student.AverageScore >= sc.MinScore &&
        //    student.AverageScore <= sc.MaxScore)?.Grade ?? "F";

        //student.FinalGrade = grade;

        //go to position of char in the string classNumber
        int digitCount = 0;
        foreach (char c in student.Class)
        {
            if (char.IsDigit(c))
                digitCount++;
            else
                break;
        }

        //get the value from begining to the position got before
        string searchKey = student.Class[..digitCount];

        if (gradeDict.TryGetValue(searchKey, out var classSchemas))
        {
            student.FinalGrade = classSchemas.FirstOrDefault(sc =>
            student.AverageScore >= sc.MinScore &&
            student.AverageScore <= sc.MaxScore)?.Grade ?? "F";
        }
    }
}

app.Run("http://localhost:5000");